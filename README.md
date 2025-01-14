# EPiServer.Labs.Find.Toolbox

Please note that this project is not officially supported by Episerver just like most EPiServer.Labs projects.
Should be considered stable and is currently used in production environments.

What you get
* An improved synonym implementation 
* An overall relevance improvement by utilising MatchPhrase,  MatchPhrasePrefix and MatchPrefix
* Support for MinimumShouldMatch which improves search experience further
* FuzzyMatch and WildcardMatch improving searches with typos and partial words

All can be used together or independently and depends on the .For() call which spawns the original queryStringQuery.


## .UsingSynonymsImproved()
The improved synonym implementation solves limitations in the following scenarios:
* Missing or unexplainable hits when using .WithAndAsDefaultOperator()
* Multiple term synonyms
* Multiple term synonyms bidirectional
* Multiple term synonyms within quotes
* Multiple term synonyms requires all terms to match
* Does not rely on an synonym index to be up to date
* No unwanted built-in synonyms

Currently the synonym expansion is done in backend (Elastic Search) and relies on a synonym index.
We solve this by retrieving and caching the synonym list and expand the matching synonyms on the query, on the client side.

Searching for 'episerver find' where find is a synonym for 'search & navigation" will result in 'episerver (find OR (search & navigation))'

Note!
* There will always be an OR relationship between the synonym match and the expanded synonym regardless if you use WithAndAsDefaultOperator() or MinimumShouldMatch().
* There will always be an AND relationship between terms of the phrase in the synonym match and the expanded synonym regardless if you use OR.

## .MinimumShouldMatch()
With MinimumShouldMatch it's possible to set or or more conditions for how many terms (in percentage and absolutes) should match.
If you specify 2<60% all terms up to 2 terms will be required to match. More than 2 terms 60% of the terms are required to match.
If you specify 2 all terms up to 2 terms will be required to match.
This is prefered over using purely OR or AND where you will either get too many hits (OR) or no hits (AND).
MinimumShouldMatch() has to be called before calling UsingImprovedSynonyms() to be utilized.

## .UsingRelevanceImproved()
UsingRelevanceImproved() applies Elastic Search's MatchPrefix, MatchPhrase, MatchPrefixPhrase for new subqueries by using the query generated by For() and/or .UsingSynonyms()
to improve the search relevance for a handful of common search patterns. This also includes the synonym expanded query variations.

## .FuzzyMatch() and .WildcardMatch()
FuzzyMatch() finds terms even if the wording is not quite right. WildcardMatch() find terms even if they are not completed or are part of another word. 
The two latter are only applied to terms longer than 2 characters and only the first 3 terms out of these. Wildcard is only added to the right. 
FuzzyMatch() gets a negative boost. Wildcard matches gets a slightly higher negative boost.

Note!
WildcardQuery and FuzzyQuery should be considered heavy for the backend and should only be used on few fields and only on fields with little content.
FuzzyMatch() does not use a true FuzzyQuery but a QueryStringQuery with fuzzysupport which gives better relevance than multiple FuzzyQuery queries.




[MatchPhrase documentation](https://www.elastic.co/guide/en/elasticsearch/reference/current/query-dsl-match-query-phrase.html)

[MatchQueryPhrase documentation](https://www.elastic.co/guide/en/elasticsearch/reference/current/query-dsl-match-query-phrase-prefix.html)

[FuzzyQuery documentation](https://www.elastic.co/guide/en/elasticsearch/reference/current/query-dsl-fuzzy-query.html)

[WildcardQuery documentation](https://www.elastic.co/guide/en/elasticsearch/reference/current/query-dsl-wildcard-query.html)


[![License](http://img.shields.io/:license-apache-blue.svg?style=flat-square)](http://www.apache.org/licenses/LICENSE-2.0.html)

---

## Table of Contents

- [System requirements](#system-requirements)
- [Installation](#installation)

---

## System requirements

* Find 12 or higher
* .NET Framework 4.6.1 or higher

See also the general [Episerver system requirements](https://world.episerver.com/documentation/system-requirements/) on Episerver World.

---

## Installation

1. Copy all files into your project or install NuGet package

2. Make sure you use 

   ```csharp
   using EPiServer.Find.Cms;
   ``` 
3. Remove any use of .UsingSynonyms()

4. Add .WithAndAsDefaultOperator if you want but we recommend .MinimumShouldMatch(). Not specifying either will allow for OR as the default operator.
   Using MinimumShouldMatch() will preced any use of .WithAndAsDefaultOperator() or the default OR.

5. Add .UsingSynonymsImproved([cacheDuration])
   The cache duration parameter defaults to 3 hours but could be set to something shorter during testing.

6. Add .UsingRelevanceImproved([fields to query])

7. It could look like this

    ```csharp
    // With MinimumShouldMatch() with conditions
    UnifiedSearchResults results = SearchClient.Instance.UnifiedSearch(Language.English)
                                    .For(query)             
                                    .MinimumShouldMatch("2<60%")
                                    .UsingSynonymsImproved() 
                                    .UsingRelevanceImproved(x => x.SearchTitle)
                                    .GetResult();
    ```
    
    ```csharp
    // With MinimumShouldMatch() absolutes    
    UnifiedSearchResults results = SearchClient.Instance.UnifiedSearch(Language.English)
                                    .For(query)             
                                    .MinimumShouldMatch("2")
                                    .UsingSynonymsImproved() 
                                    .UsingRelevanceImproved(x => x.SearchTitle)
                                    .GetResult();
    ```
    
    ```csharp
    // With WithAndAsDefaultOperator() 
    UnifiedSearchResults results = SearchClient.Instance.UnifiedSearch(Language.English)
                                    .For(query)             
                                    .WithAndAsDefaultOperator()
                                    .UsingSynonymsImproved()                                     
                                    .UsingRelevanceImproved(x => x.SearchTitle)
                                    .GetResult();
    ```

    ```csharp
    // Without WithAndAsDefaultOperator() which is the default behaviour which sets the default operator to OR
    UnifiedSearchResults results = SearchClient.Instance.UnifiedSearch(Language.English)
                                    .For(query)                 
                                    .UsingSynonymsImproved()                                         
                                    .UsingRelevanceImproved(x => x.SearchTitle)
                                    .GetResult();
    ```

    ```csharp
    // Boosting matches for phrase searches overall and in beginning of fields
    UnifiedSearchResults results = SearchClient.Instance.UnifiedSearch(Language.English)
                                    .For(query)       
                                    .MinimumShouldMatch("2")
                                    .UsingSynonymsImproved()      
                                    .UsingRelevanceImproved(x => x.SearchTitle)
                                    .GetResult();
    ```

    ```csharp
    // Let clients do some typos and write half-words and still get some hits
    UnifiedSearchResults results = SearchClient.Instance.UnifiedSearch(Language.English)
                                    .For(query)       
                                    .MinimumShouldMatch("2")
                                    .UsingSynonymsImproved()
                                    .UsingRelevanceImproved(x => x.SearchTitle)
                                    .FuzzyMatch(x => x.SearchTitle)
                                    .WildcardMatch(x => x.SearchTitle)
                                    .GetResult();
    ```


7. Enjoy!

