namespace EVChargeSwap.Features.Locations.Search;

internal static class LocationSearchMapping
{
    public const string Definition = """
        {
          "settings": {
            "analysis": {
              "analyzer": {
                "station_text": {
                  "tokenizer": "standard",
                  "filter": ["lowercase", "asciifolding"]
                }
              }
            }
          },
          "mappings": {
            "dynamic": "strict",
            "properties": {
              "id": { "type": "keyword" },
              "name": { "type": "search_as_you_type", "analyzer": "station_text" },
              "address": { "type": "search_as_you_type", "analyzer": "station_text" },
              "location": { "type": "geo_point" }
            }
          }
        }
        """;
}
