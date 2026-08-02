using System.Collections.Generic;

namespace Ponder
{
    public class PonderSubject
    {
        public string Title { get; }
        public string Description { get; }

        public PonderSubject(string title, string description)
        {
            Title = title;
            Description = description;
        }
    }

    public static class PonderRegistry
    {
        private static readonly Dictionary<string, PonderSubject> subjects = new Dictionary<string, PonderSubject>();

        public static void Register(string eventName, PonderSubject subject)
        {
            subjects[eventName] = subject;
        }

        public static PonderSubject GetForEvent(string eventName)
        {
            if (string.IsNullOrEmpty(eventName) || !subjects.TryGetValue(eventName, out var subject))
            {
                return null;
            }
            return subject;
        }
    }
}
