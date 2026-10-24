namespace Noir;
// One watched movie or episode read from another app, before it is matched to a title.
// Source, LoggedAt, Note and ShowRating only come from Noir's own lossless JSON export.
public sealed record ImportRow(string Service,string Title,int Year,bool? IsMovie,string ImdbId,string TvdbId,int Season,int Episode,string EpisodeTitle,DateTimeOffset? WatchedAt,int Rating,bool Approximate=false,string Source="",DateTimeOffset? LoggedAt=null,string Note="",int ShowRating=0);
