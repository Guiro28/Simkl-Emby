using System;
using System.Globalization;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Simkl.Api.Objects;
using Simkl.Configuration;

namespace Simkl.Helpers
{
    /// <summary>Builders and guards shared by the mediator and the scheduled tasks.</summary>
    internal static class SyncHelper
    {
        public static int? ToInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;
        }

        public static string ToIso(DateTimeOffset? date)
        {
            return date?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        public static SyncIds MovieIds(BaseItem movie)
        {
            return new SyncIds
            {
                imdb = NullIfEmpty(movie.GetProviderId(MetadataProviders.Imdb)),
                tmdb = ToInt(movie.GetProviderId(MetadataProviders.Tmdb))
            };
        }

        public static SyncIds ShowIds(BaseItem series)
        {
            return new SyncIds
            {
                imdb = NullIfEmpty(series.GetProviderId(MetadataProviders.Imdb)),
                tmdb = ToInt(series.GetProviderId(MetadataProviders.Tmdb)),
                tvdb = ToInt(series.GetProviderId(MetadataProviders.Tvdb)),
                tvrage = ToInt(series.GetProviderId(MetadataProviders.TvRage))
            };
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        /// <summary>
        /// True when the item lives in a monitored (not excluded) location and carries
        /// identifiers Simkl can resolve. Mirrors the Trakt plugin's CanSync.
        /// </summary>
        public static bool CanSync(BaseItem item, UserConfig config, IFileSystem fileSystem)
        {
            if (item?.Path == null || item.LocationType == LocationType.Virtual)
                return false;

            if (config.locationsExcluded != null && fileSystem != null &&
                config.locationsExcluded.Any(s =>
                    !string.IsNullOrWhiteSpace(s) && fileSystem.ContainsSubPath(s, item.Path)))
                return false;

            if (item is Movie movie)
                return !string.IsNullOrEmpty(movie.GetProviderId(MetadataProviders.Imdb)) ||
                       !string.IsNullOrEmpty(movie.GetProviderId(MetadataProviders.Tmdb));

            if (item is Episode episode && episode.Series != null && !episode.IsMissingEpisode &&
                (episode.IndexNumber.HasValue || !string.IsNullOrEmpty(episode.GetProviderId(MetadataProviders.Tvdb))))
            {
                var series = episode.Series;
                return !string.IsNullOrEmpty(series.GetProviderId(MetadataProviders.Imdb)) ||
                       !string.IsNullOrEmpty(series.GetProviderId(MetadataProviders.Tvdb)) ||
                       !string.IsNullOrEmpty(series.GetProviderId(MetadataProviders.Tmdb));
            }

            if (item is Series show)
                return !string.IsNullOrEmpty(show.GetProviderId(MetadataProviders.Imdb)) ||
                       !string.IsNullOrEmpty(show.GetProviderId(MetadataProviders.Tvdb)) ||
                       !string.IsNullOrEmpty(show.GetProviderId(MetadataProviders.Tmdb));

            return false;
        }

        public static int GetSeasonNumber(Episode episode)
        {
            return episode.ParentIndexNumber == 0 ? 0 : (episode.ParentIndexNumber ?? 1);
        }

        /// <summary>Splits a list into consecutive chunks of at most <paramref name="size"/> items.</summary>
        public static System.Collections.Generic.IEnumerable<System.Collections.Generic.List<T>> Chunk<T>(
            System.Collections.Generic.IReadOnlyList<T> source, int size)
        {
            for (int i = 0; i < source.Count; i += size)
            {
                var chunk = new System.Collections.Generic.List<T>(size);
                for (int j = i; j < i + size && j < source.Count; j++) chunk.Add(source[j]);
                yield return chunk;
            }
        }
    }
}
