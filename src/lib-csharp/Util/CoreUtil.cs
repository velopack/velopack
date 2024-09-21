using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Util
{
    internal static class CoreUtil
    {
        public const string SpecVersionFileName = "sq.version";

        public static string GetVeloReleaseIndexName(string channel)
        {
            return $"releases.{channel ?? VelopackRuntimeInfo.SystemOs.GetOsShortName()}.json";
        }

        public static string GetReleasesFileName(string? channel)
        {
            if (channel == null) {
                // default RELEASES file name for each platform.
                if (VelopackRuntimeInfo.IsOSX) return "RELEASES-osx";
                if (VelopackRuntimeInfo.IsLinux) return "RELEASES-linux";
                return "RELEASES";
            } else {
                // if the channel is an empty string or "win", we use the default RELEASES file name.
                if (String.IsNullOrWhiteSpace(channel) || channel.ToLower() == "win") {
                    return "RELEASES";
                }

                // all other cases the RELEASES file includes the channel name.
                return $"RELEASES-{channel.ToLower()}";
            }
        }

        public static string GetAppUserModelId(string packageId)
        {
            return $"velopack.{packageId}";
        }

        /// <summary>
        /// Calculates the total percentage of a specific step that should report within a specific range.
        /// <para />
        /// If a step needs to report between 50 -> 75 %, this method should be used as CalculateProgress(percentage, 50, 75). 
        /// </summary>
        /// <param name="percentageOfCurrentStep">The percentage of the current step, a value between 0 and 100.</param>
        /// <param name="stepStartPercentage">The start percentage of the range the current step represents.</param>
        /// <param name="stepEndPercentage">The end percentage of the range the current step represents.</param>
        /// <returns>The calculated percentage that can be reported about the total progress.</returns>
        public static int CalculateProgress(int percentageOfCurrentStep, int stepStartPercentage, int stepEndPercentage)
        {
            // Ensure we are between 0 and 100
            percentageOfCurrentStep = Math.Max(Math.Min(percentageOfCurrentStep, 100), 0);

            var range = stepEndPercentage - stepStartPercentage;
            var singleValue = range / 100d;
            var totalPercentage = (singleValue * percentageOfCurrentStep) + stepStartPercentage;

            return (int) totalPercentage;
        }

        public static Action<int> CreateProgressDelegate(Action<int> rootProgress, int stepStartPercentage, int stepEndPercentage)
        {
            return percentage => {
                rootProgress(CalculateProgress(percentage, stepStartPercentage, stepEndPercentage));
            };
        }

        public static string RemoveByteOrderMarkerIfPresent(string content)
        {
            return string.IsNullOrEmpty(content)
                ? string.Empty
                : RemoveByteOrderMarkerIfPresent(Encoding.UTF8.GetBytes(content));
        }

        public static string RemoveByteOrderMarkerIfPresent(byte[] content)
        {
            byte[] output = { };

            Func<byte[], byte[], bool> matches = (bom, src) => {
                if (src.Length < bom.Length) return false;

                return !bom.Where((chr, index) => src[index] != chr).Any();
            };

            var utf32Be = new byte[] { 0x00, 0x00, 0xFE, 0xFF };
            var utf32Le = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
            var utf16Be = new byte[] { 0xFE, 0xFF };
            var utf16Le = new byte[] { 0xFF, 0xFE };
            var utf8 = new byte[] { 0xEF, 0xBB, 0xBF };

            if (matches(utf32Be, content)) {
                output = new byte[content.Length - utf32Be.Length];
            } else if (matches(utf32Le, content)) {
                output = new byte[content.Length - utf32Le.Length];
            } else if (matches(utf16Be, content)) {
                output = new byte[content.Length - utf16Be.Length];
            } else if (matches(utf16Le, content)) {
                output = new byte[content.Length - utf16Le.Length];
            } else if (matches(utf8, content)) {
                output = new byte[content.Length - utf8.Length];
            } else {
                output = content;
            }

            if (output.Length > 0) {
                Buffer.BlockCopy(content, content.Length - output.Length, output, 0, output.Length);
            }

            return Encoding.UTF8.GetString(output);
        }

        public static T GetAwaiterResult<T>(this Task<T> task)
        {
            return task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public static void GetAwaiterResult(this Task task)
        {
            task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public static bool TryParseEnumU16<TEnum>(ushort enumValue, out TEnum? retVal)
        {
            retVal = default;
            bool success = Enum.IsDefined(typeof(TEnum), enumValue);
            if (success) {
                retVal = (TEnum) Enum.ToObject(typeof(TEnum), enumValue);
            }

            return success;
        }

        public static TEnum[] GetEnumValues<TEnum>() where TEnum : struct, Enum
        {
#if NET6_0_OR_GREATER
            return Enum.GetValues<TEnum>();
#else
            return Enum.GetValues(typeof(TEnum)).Cast<TEnum>().ToArray();
#endif
        }
    }
}