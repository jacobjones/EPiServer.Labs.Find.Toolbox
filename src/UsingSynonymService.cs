﻿using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using EPiServer.Find.Helpers.Text;
using EPiServer.Find.Tracing;
using EPiServer.ServiceLocation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EPiServer.Find.Cms
{
    [ServiceConfiguration(Lifecycle = ServiceInstanceScope.Singleton)]
    public class UsingSynonymService
    {
        private readonly SynonymLoader _synonymLoader;

        public UsingSynonymService(SynonymLoader synonymLoader)
        {
            _synonymLoader = synonymLoader;
        }

        public IQueriedSearch<TSource, QueryStringQuery> UsingSynonyms<TSource>(IQueriedSearch<TSource> search, TimeSpan? cacheDuration = null)
        {

            if (search.Client.Settings.Admin)
            {
                return new Search<TSource, QueryStringQuery>(search, context =>
                {
                    if (context.RequestBody.Query != null)
                    {
                        
                        BoolQuery newBoolQuery = new BoolQuery();
                        BoolQuery currentBoolQuery;
                        MultiFieldQueryStringQuery currentQueryStringQuery;


                        if (QueryHelpers.TryGetBoolQuery(context.RequestBody.Query, search, out currentBoolQuery))
                        {
                            if (!QueryHelpers.TryGetQueryStringQuery(currentBoolQuery.Should[0], search, out currentQueryStringQuery))
                            {
                                return;
                            }
                            currentBoolQuery.Should.RemoveAt(0); // Remove the QueryStringQuery generated by For()
                        }
                        else
                        {                            
                            if (!QueryHelpers.TryGetQueryStringQuery(context.RequestBody.Query, search, out currentQueryStringQuery))
                            {
                                return;
                            }                            
                        }
                                                                                                          
                        var query = QueryHelpers.GetQueryString(currentQueryStringQuery);
                        if (query.IsNullOrEmpty())
                        {
                            return;
                        }

                        // If MinimumShouldMatch has been set previously pick up the minShouldMatch value
                        MinShouldMatchQueryStringQuery currentMinShouldMatchQueryStringQuery;
                        string minShouldMatch = "";
                        if (QueryHelpers.TryGetMinShouldMatchQueryStringQuery(currentQueryStringQuery, search, out currentMinShouldMatchQueryStringQuery))
                        {
                            minShouldMatch = currentMinShouldMatchQueryStringQuery.MinimumShouldMatch;
                        }

                        var synonymDictionary = _synonymLoader.GetSynonyms(cacheDuration);

                        var queryPhrases = QueryHelpers.GetQueryPhrases(query).ToArray();                   
                        if (queryPhrases.Count() == 0)
                        {
                            return;
                        }

                        var phraseVariations = GetPhraseVariations(queryPhrases);                                   // 'Alloy tech now' would result in Alloy, 'Alloy tech', 'Alloy tech now', tech, 'tech now' and now                        
                        var phrasesToExpand = GetPhrasesToExpand(phraseVariations, synonymDictionary);              // Return all phrases with expanded synonyms                        
                        var nonExpandedPhrases = GetPhrasesNotToExpand(queryPhrases, phrasesToExpand);              // Return all phrases that didn't get expanded
                        var expandedPhrases = ExpandPhrases(phrasesToExpand, synonymDictionary);                    // Expand phrases                                                        


                        // Add query for non expanded phrases                        
                        if (nonExpandedPhrases.Count() > 0)
                        {
                            var nonExpandedPhraseQuery = CreateQuery(string.Join(" ", nonExpandedPhrases), currentQueryStringQuery, "");

                            // MinimumShouldMatch() overrides WithAndAsDefaultOperator()
                            if (minShouldMatch.IsNotNullOrEmpty())
                            {
                                nonExpandedPhraseQuery.MinimumShouldMatch = minShouldMatch;
                            }
                            // Emulate WithAndAsDefaultOperator() using MinimumShouldMatch set to 100%
                            else if (currentQueryStringQuery.DefaultOperator == BooleanOperator.And)
                            {
                                nonExpandedPhraseQuery.MinimumShouldMatch = "100%";
                            }

                            newBoolQuery.Should.Add(nonExpandedPhraseQuery);
                        }

                        // Expanded phrases only requires one term to match
                        if (expandedPhrases.Count() > 0)
                        {
                            var expandedPhraseQuery = CreateQuery(string.Join(" ", expandedPhrases), currentQueryStringQuery, "1");                            
                            newBoolQuery.Should.Add(expandedPhraseQuery);
                        }
                                           
                        if (newBoolQuery.IsNull())
                        {
                            return;
                        }

                        if (currentBoolQuery.IsNotNull())
                        {
                            foreach (var currentQuery in currentBoolQuery.Should) {
                                newBoolQuery.Should.Add(currentQuery);
                            }
                        }

                        context.RequestBody.Query = newBoolQuery;

                    }
                });
            }
            else
            {                
                Find.Tracing.Trace.Instance.Add(new TraceEvent(search, "Your index does not support synonyms. Please contact support to have your account upgraded. Falling back to search without synonyms.") { IsError = false });
                return new Search<TSource, QueryStringQuery>(search, context => { });
            }
        }

        private static MinShouldMatchQueryStringQuery CreateQuery(string phrase, MultiFieldQueryStringQuery currentQueryStringQuery, string minShouldMatch)
        {
            string phrasesQuery = QueryHelpers.EscapeElasticSearchQuery(phrase);
            var minShouldMatchQuery = new MinShouldMatchQueryStringQuery(phrasesQuery);

            minShouldMatchQuery.RawQuery = currentQueryStringQuery.RawQuery;
            minShouldMatchQuery.AllowLeadingWildcard = currentQueryStringQuery.AllowLeadingWildcard;
            minShouldMatchQuery.AnalyzeWildcard = currentQueryStringQuery.AnalyzeWildcard;
            minShouldMatchQuery.Analyzer = currentQueryStringQuery.Analyzer;
            minShouldMatchQuery.AutoGeneratePhraseQueries = currentQueryStringQuery.AutoGeneratePhraseQueries;
            minShouldMatchQuery.Boost = currentQueryStringQuery.Boost;
            minShouldMatchQuery.EnablePositionIncrements = currentQueryStringQuery.EnablePositionIncrements;
            minShouldMatchQuery.FuzzyMinSim = currentQueryStringQuery.FuzzyMinSim;
            minShouldMatchQuery.FuzzyPrefixLength = currentQueryStringQuery.FuzzyPrefixLength;
            minShouldMatchQuery.LowercaseExpandedTerms = currentQueryStringQuery.LowercaseExpandedTerms;
            minShouldMatchQuery.PhraseSlop = currentQueryStringQuery.PhraseSlop;
            minShouldMatchQuery.DefaultField = currentQueryStringQuery.DefaultField;
            minShouldMatchQuery.Fields = currentQueryStringQuery.Fields;

            minShouldMatchQuery.MinimumShouldMatch = minShouldMatch.IsNotNullOrEmpty() ? minShouldMatch : "1";
            minShouldMatchQuery.DefaultOperator = BooleanOperator.Or;            

            return minShouldMatchQuery;
        }

        // Return all combinations of phrases in order
        private static HashSet<string> GetPhraseVariations(string[] terms)
        {
            HashSet<string> candidates = new HashSet<string>();

            for (var s = 0; s <= terms.Count(); s++)
            {
                for (var c = 1; c <= terms.Count() - s; c++)
                {
                    var term = string.Join(" ", terms.Skip(s).Take(c));
                    candidates.Add(term);
                }
            }

            return candidates;
        }

        // Get phrase (not variations) that should not get expanded
        private static HashSet<string> GetPhrasesNotToExpand(string[] terms, HashSet<string> phrasesToExpand)
        {
            string[] phrasesNotToExpand = terms.Except(phrasesToExpand).ToArray();

            // Exclude phrases that didn't get expanded but share terms with phrasesToExpand
            foreach (var phraseToExpand in phrasesToExpand)
            {
                phrasesNotToExpand = phrasesNotToExpand.Except(phraseToExpand.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries)).ToArray();
            }

            return new HashSet<string>(phrasesNotToExpand);
        }

        // Get phrase variations that should get expanded (that match synonyms)
        private static HashSet<string> GetPhrasesToExpand(HashSet<string> phraseVariations, Dictionary<String, HashSet<String>> synonymDictionary)
        {
            return new HashSet<string>(phraseVariations.Intersect(synonymDictionary.Keys));
        }

        // Return phrases with their expanded synonym
        private static HashSet<string> ExpandPhrases(HashSet<string> phrasesToExpand, Dictionary<String, HashSet<String>> synonymDictionary)
        {
            HashSet<string> queryList = new HashSet<string>();

            foreach (var match in phrasesToExpand)
            {                
                queryList.Add(ExpandPhrase(match, synonymDictionary[match]));
            }

            return queryList;
        }

        // Return phrase expanded with matching synonym
        // Searching for 'dagis' where 'dagis' is a synonym for 'förskola' and 'lekis'
        // we will get the following expansion (dagis OR (förskola AND lekis))
        private static string ExpandPhrase(string phrase, HashSet<string> synonyms)
        {
            HashSet<string> expandedPhrases = new HashSet<string>();

            //Insert AND in between terms if not quoted
            if (!IsStringQuoted(phrase))
            {
                phrase = phrase.Replace(" ", string.Format(" {0} ", "AND"));
            }

            foreach (var synonym in synonyms)
            {
                
                //Insert AND in between terms if not quoted. Quoted not yet allowed by the Find UI though.
                if (!IsStringQuoted(synonym))
                {
                    expandedPhrases.Add(string.Format("({0}) OR ({1})", phrase, synonym.Replace(" ", string.Format(" {0} ", "AND"))));
                }
                else
                {
                    expandedPhrases.Add(string.Format("({0}) OR ({1})", phrase, synonym));
                }

            }

            return string.Format("({0})",string.Join(" OR ", expandedPhrases));
        }

        private static bool IsStringQuoted(string text)
        {
            return (text.StartsWith("\"") && text.EndsWith("\""));
        }

        private static bool ContainsMultipleTerms(string text)
        {
            return (text.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries).Count() > 1);
        }

    }
}