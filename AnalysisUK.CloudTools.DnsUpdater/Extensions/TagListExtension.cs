using System.Collections.Generic;
using Amazon.EC2.Model;

namespace AnalysisUK.CloudTools.DnsUpdater.Extensions
{
    public static class TagListExtension
    {
        public static string GetTag(this List<Tag> tags, string name)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == name)
                {
                    return tag.Value;
                }
            }

            return null;
        }
    }
}