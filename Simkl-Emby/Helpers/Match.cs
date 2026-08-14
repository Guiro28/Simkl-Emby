using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Simkl.Api.Objects;

namespace Simkl.Helpers
{
    /// <summary>Matches Emby library items against data returned by Simkl.</summary>
    internal static class Match
    {
        private static bool Eq(string a, string b)
            => !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public static bool IsMovieMatch(BaseItem item, SimklReadIds ids)
        {
            if (ids == null) return false;
            return Eq(item.GetProviderId(MetadataProviders.Imdb), ids.imdb)
                || Eq(item.GetProviderId(MetadataProviders.Tmdb), ids.tmdb);
        }

        public static bool IsShowMatch(BaseItem series, SimklReadIds ids)
        {
            if (ids == null) return false;
            return Eq(series.GetProviderId(MetadataProviders.Tvdb), ids.tvdb)
                || Eq(series.GetProviderId(MetadataProviders.Imdb), ids.imdb)
                || Eq(series.GetProviderId(MetadataProviders.Tmdb), ids.tmdb);
        }

        public static AllItemsMovie FindMovie(BaseItem item, IEnumerable<AllItemsMovie> results)
            => results?.FirstOrDefault(i => IsMovieMatch(item, i.movie?.ids));

        public static AllItemsShow FindShow(Series series, IEnumerable<AllItemsShow> results)
            => results?.FirstOrDefault(i => IsShowMatch(series, i.show?.ids));

        public static PlaybackSession FindMoviePlayback(BaseItem item, IEnumerable<PlaybackSession> results)
            => results?.FirstOrDefault(i => i.movie != null && IsMovieMatch(item, i.movie.ids));

        public static PlaybackSession FindEpisodePlayback(Episode episode, IEnumerable<PlaybackSession> results)
        {
            if (results == null || episode?.Series == null) return null;
            return results.FirstOrDefault(i =>
                i.episode != null && i.show != null &&
                IsShowMatch(episode.Series, i.show.ids) &&
                i.episode.season == SyncHelper.GetSeasonNumber(episode) &&
                i.episode.EpisodeNumber == episode.IndexNumber);
        }
    }
}
