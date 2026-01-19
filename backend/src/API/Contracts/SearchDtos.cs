namespace API.Contracts;

/// Some matches are too minor to show in sidebar list. MatchLocation controls visibility.
public record SearchResultDto(
    string Id,
    string Type,
    string Name,
    string? MatchedText,
    string? IconPath,
    string MatchLocation
);

public record SearchResponse(
    string Query,
    IReadOnlyList<SearchResultDto> Results,
    int TotalResults
);
