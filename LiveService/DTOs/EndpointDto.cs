namespace LiveService.DTOs;

public record LivestreamEndpointDto(
    string? IngestUrl,
    PlaybackEndpointDto PlaybackEndpoints
);

public record PlaybackEndpointDto(
    string RtmpUrl,
    string? HttpFlvUrl = null
);
