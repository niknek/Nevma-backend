using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nevma.Commands.Api.Domain;
using Nevma.Contracts.Commands;

namespace Nevma.Commands.Api.Application;

public sealed partial class BilingualTemporalNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, DayOfWeek> Weekdays = new Dictionary<string, DayOfWeek>
    {
        ["monday"] = DayOfWeek.Monday, ["δευτερα"] = DayOfWeek.Monday,
        ["tuesday"] = DayOfWeek.Tuesday, ["τριτη"] = DayOfWeek.Tuesday,
        ["wednesday"] = DayOfWeek.Wednesday, ["τεταρτη"] = DayOfWeek.Wednesday,
        ["thursday"] = DayOfWeek.Thursday, ["πεμπτη"] = DayOfWeek.Thursday,
        ["friday"] = DayOfWeek.Friday, ["παρασκευη"] = DayOfWeek.Friday,
        ["saturday"] = DayOfWeek.Saturday, ["σαββατο"] = DayOfWeek.Saturday,
        ["sunday"] = DayOfWeek.Sunday, ["κυριακη"] = DayOfWeek.Sunday
    };

    public TemporalNormalizationResult Normalize(
        string message,
        ParsedCommand command,
        DateTimeOffset localNow,
        string detectedLanguage,
        IReadOnlyCollection<AssistantContact>? contacts = null)
    {
        if (command.Intent is not (CommandIntent.CreateCalendarEvent or CommandIntent.UpdateCalendarEvent))
            return new TemporalNormalizationResult(command, null);

        var normalized = RemoveDiacritics(message).ToLowerInvariant();
        var relativeDate = ResolveRelativeDate(normalized, localNow);
        var explicitTime = ResolveTime(normalized);
        if (relativeDate is not null && explicitTime is null)
            return new TemporalNormalizationResult(null, detectedLanguage == "el"
                ? "Ποια ακριβώς ώρα θέλεις να γίνει το ραντεβού;"
                : "What exact time should the appointment start?", true);

        try
        {
            return command.Intent == CommandIntent.CreateCalendarEvent
                ? NormalizeCreate(command, relativeDate, explicitTime, localNow, message, detectedLanguage, contacts)
                : NormalizeUpdate(command, relativeDate, explicitTime, localNow, message, contacts);
        }
        catch (JsonException)
        {
            return new TemporalNormalizationResult(command, null);
        }
    }

    public TemporalNormalizationResult TryCreateFallback(
        string message,
        DateTimeOffset localNow,
        string detectedLanguage,
        IReadOnlyCollection<AssistantContact>? contacts = null)
    {
        var normalized = RemoveDiacritics(message).ToLowerInvariant();
        var isGreekRequest = GreekCreateIntent().IsMatch(normalized);
        var isEnglishRequest = EnglishCreateIntent().IsMatch(normalized);
        if (!isGreekRequest && !isEnglishRequest)
            return new TemporalNormalizationResult(null, null);

        var date = ResolveRelativeDate(normalized, localNow);
        var time = ResolveTime(normalized);
        if (date is null || time is null)
        {
            var clarification = detectedLanguage == "el"
                ? date is null && time is null
                    ? "Ποια ακριβώς ημέρα και ώρα θέλεις να γίνει το ραντεβού;"
                    : date is null ? "Ποια ακριβώς ημέρα θέλεις να γίνει το ραντεβού;" : "Ποια ακριβώς ώρα θέλεις να γίνει το ραντεβού;"
                : date is null && time is null
                    ? "What exact date and time should the appointment start?"
                    : date is null ? "What exact date should the appointment start?" : "What exact time should the appointment start?";
            return new TemporalNormalizationResult(null, clarification, true);
        }

        var unlinkedContacts = ResolveMentionedUnlinkedContacts(message, contacts);
        if (unlinkedContacts.Count > 0)
        {
            var names = string.Join(", ", unlinkedContacts);
            return new TemporalNormalizationResult(null, detectedLanguage == "el"
                ? $"Η επαφή {names} δεν έχει ακόμη συνδεδεμένο λογαριασμό Nevma. Για να λάβει notification και να αποδεχτεί το ραντεβού, πρέπει να εγγραφεί με το ίδιο email ή τηλέφωνο."
                : $"{names} is not linked to a Nevma account yet. They must register with the same email or phone to receive and accept the invitation.");
        }

        var title = ChooseTitle(null, message, detectedLanguage);
        var startsAt = new DateTimeOffset(date.Value.ToDateTime(time.Value), localNow.Offset);
        var arguments = new CreateCalendarEventCommandArguments(
            title, message.Trim(), null, startsAt, startsAt.AddHours(1), ResolveParticipantIds(message, contacts));
        var command = new ParsedCommand(
            CommandIntent.CreateCalendarEvent,
            detectedLanguage == "el" ? $"Δημιουργία: {title}" : $"Create: {title}",
            JsonSerializer.Serialize(arguments, JsonOptions));
        return new TemporalNormalizationResult(command, null);
    }

    private static TemporalNormalizationResult NormalizeCreate(
        ParsedCommand command,
        DateOnly? date,
        TimeOnly? time,
        DateTimeOffset localNow,
        string message,
        string detectedLanguage,
        IReadOnlyCollection<AssistantContact>? contacts)
    {
        var unlinkedContacts = ResolveMentionedUnlinkedContacts(message, contacts);
        if (unlinkedContacts.Count > 0)
        {
            var names = string.Join(", ", unlinkedContacts);
            return new TemporalNormalizationResult(null, detectedLanguage == "el"
                ? $"Η επαφή {names} δεν έχει ακόμη συνδεδεμένο λογαριασμό Nevma. Για να λάβει notification και να αποδεχτεί το ραντεβού, πρέπει να εγγραφεί με το ίδιο email ή τηλέφωνο."
                : $"{names} is not linked to a Nevma account yet. They must register with the same email or phone to receive and accept the invitation.");
        }

        var request = JsonSerializer.Deserialize<CreateCalendarEventCommandArguments>(command.ArgumentsJson, JsonOptions);
        if (request is null) return new TemporalNormalizationResult(command, null);
        var (startsAt, endsAt) = NormalizeRange(request.StartsAt, request.EndsAt, date, time, localNow.Offset);
        var title = ChooseTitle(request.Title, message, detectedLanguage);
        var participants = ResolveParticipantIds(message, contacts);
        var json = JsonSerializer.Serialize(request with
        {
            Title = title,
            StartsAt = startsAt,
            EndsAt = endsAt,
            ParticipantIds = participants.Count > 0 ? participants : request.ParticipantIds
        }, JsonOptions);
        var summary = detectedLanguage == "el" ? $"Δημιουργία: {title}" : $"Create: {title}";
        return new TemporalNormalizationResult(command with { Summary = summary, ArgumentsJson = json }, null);
    }

    private static string ChooseTitle(string? proposedTitle, string message, string detectedLanguage)
    {
        var normalized = RemoveDiacritics(message).ToLowerInvariant();
        var isGreek = detectedLanguage == "el" || GreekCharacters().IsMatch(message);

        if (normalized.Contains("καφε", StringComparison.Ordinal) || normalized.Contains("coffee", StringComparison.Ordinal))
        {
            var coffeeMatch = CoffeeSubject().Match(message);
            if (coffeeMatch.Success)
            {
                var subject = coffeeMatch.Groups["subject"].Value.Trim(' ', '.', ',', ';', ':');
                if (subject.Length is > 0 and <= 45)
                    return isGreek ? $"Καφέ με {subject}" : $"Coffee with {subject}";
            }

            return isGreek ? "Καφέ" : "Coffee";
        }

        if (normalized.Contains("οδοντιατρ", StringComparison.Ordinal)) return "Ραντεβού με οδοντίατρο";
        if (normalized.Contains("dentist", StringComparison.Ordinal)) return "Dentist appointment";
        if (normalized.Contains("κομμωτηρ", StringComparison.Ordinal)) return "Ραντεβού στο κομμωτήριο";
        if (normalized.Contains("hairdresser", StringComparison.Ordinal) || normalized.Contains("haircut", StringComparison.Ordinal))
            return "Hair appointment";
        if (normalized.Contains("λογιστ", StringComparison.Ordinal)) return "Ραντεβού με λογιστή";
        if (normalized.Contains("accountant", StringComparison.Ordinal)) return "Meeting with accountant";
        if (normalized.Contains("γιατρ", StringComparison.Ordinal)) return "Ραντεβού με γιατρό";
        if (normalized.Contains("doctor", StringComparison.Ordinal)) return "Doctor appointment";

        var match = isGreek ? GreekSubject().Match(message) : EnglishSubject().Match(message);
        if (match.Success)
        {
            var subject = match.Groups["subject"].Value.Trim(' ', '.', ',', ';', ':');
            if (subject.Length is > 0 and <= 45)
                return isGreek ? $"Συνάντηση με {subject}" : $"Meeting with {subject}";
        }

        var cleanProposal = proposedTitle?.Trim();
        if (!string.IsNullOrWhiteSpace(cleanProposal) && cleanProposal.Length <= 60 &&
            !TemporalTitleWords().IsMatch(RemoveDiacritics(cleanProposal).ToLowerInvariant()))
            return cleanProposal;

        return isGreek ? "Ραντεβού" : "Appointment";
    }

    private static TemporalNormalizationResult NormalizeUpdate(
        ParsedCommand command,
        DateOnly? date,
        TimeOnly? time,
        DateTimeOffset localNow,
        string message,
        IReadOnlyCollection<AssistantContact>? contacts)
    {
        var request = JsonSerializer.Deserialize<UpdateCalendarEventCommandArguments>(command.ArgumentsJson, JsonOptions);
        if (request is null) return new TemporalNormalizationResult(command, null);
        var (startsAt, endsAt) = NormalizeRange(request.StartsAt, request.EndsAt, date, time, localNow.Offset);
        var participants = ResolveParticipantIds(message, contacts);
        var json = JsonSerializer.Serialize(request with
        {
            StartsAt = startsAt,
            EndsAt = endsAt,
            ParticipantIds = participants.Count > 0 ? participants : request.ParticipantIds
        }, JsonOptions);
        return new TemporalNormalizationResult(command with { ArgumentsJson = json }, null);
    }

    private static IReadOnlyCollection<Guid> ResolveParticipantIds(
        string message,
        IReadOnlyCollection<AssistantContact>? contacts)
    {
        if (contacts is null || contacts.Count == 0) return [];
        var normalizedMessage = RemoveDiacritics(message).ToLowerInvariant();
        var transliteratedMessage = TransliterateGreek(normalizedMessage);
        var aliases = BuildAliases(contacts)
            .Where(item => item.Contact.UserId.HasValue && item.Contact.UserId.Value != Guid.Empty);

        return aliases
            .Where(item => MatchesAlias(item.Alias, normalizedMessage, transliteratedMessage))
            .Select(item => item.Contact.UserId!.Value)
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyCollection<string> ResolveMentionedUnlinkedContacts(
        string message,
        IReadOnlyCollection<AssistantContact>? contacts)
    {
        if (contacts is null || contacts.Count == 0) return [];
        var normalizedMessage = RemoveDiacritics(message).ToLowerInvariant();
        var transliteratedMessage = TransliterateGreek(normalizedMessage);
        return BuildAliases(contacts)
            .Where(item => item.Contact.UserId is null && MatchesAlias(item.Alias, normalizedMessage, transliteratedMessage))
            .Select(item => item.Contact.DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(string Alias, AssistantContact Contact)> BuildAliases(
        IReadOnlyCollection<AssistantContact>? contacts) =>
        (contacts ?? [])
            .Where(contact => !string.IsNullOrWhiteSpace(contact.DisplayName))
            .SelectMany(contact =>
            {
                var fullName = RemoveDiacritics(contact.DisplayName).ToLowerInvariant().Trim();
                var firstName = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                var values = new HashSet<string>(StringComparer.Ordinal) { fullName, firstName ?? fullName };
                if (firstName is { Length: > 3 } && (firstName.EndsWith('ς') || firstName.EndsWith('σ')))
                    values.Add(firstName[..^1]);
                return values.Select(alias => (Alias: alias, Contact: contact));
            })
            .Where(item => item.Alias.Length >= 2)
            .GroupBy(item => item.Alias, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Contact.ContactId ?? item.Contact.UserId).Distinct().Count() == 1)
            .Select(group => group.First())
            .OrderByDescending(item => item.Alias.Length);

    private static bool MatchesAlias(string alias, string normalizedMessage, string transliteratedMessage) =>
        WholeWord(alias).IsMatch(normalizedMessage) || WholeWord(alias).IsMatch(transliteratedMessage);

    private static string TransliterateGreek(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(character switch
            {
                'α' => "a", 'β' => "v", 'γ' => "g", 'δ' => "d", 'ε' => "e", 'ζ' => "z",
                'η' => "i", 'θ' => "th", 'ι' => "i", 'κ' => "k", 'λ' => "l", 'μ' => "m",
                'ν' => "n", 'ξ' => "x", 'ο' => "o", 'π' => "p", 'ρ' => "r", 'σ' or 'ς' => "s",
                'τ' => "t", 'υ' => "i", 'φ' => "f", 'χ' => "ch", 'ψ' => "ps", 'ω' => "o",
                _ => character.ToString()
            });
        return builder.ToString();
    }

    private static (DateTimeOffset StartsAt, DateTimeOffset EndsAt) NormalizeRange(
        DateTimeOffset originalStart,
        DateTimeOffset originalEnd,
        DateOnly? date,
        TimeOnly? time,
        TimeSpan offset)
    {
        if (date is null && time is null) return (originalStart, originalEnd);
        var localOriginal = originalStart.ToOffset(offset);
        var targetDate = date ?? DateOnly.FromDateTime(localOriginal.Date);
        var targetTime = time ?? TimeOnly.FromDateTime(localOriginal.DateTime);
        var startsAt = new DateTimeOffset(targetDate.ToDateTime(targetTime), offset);
        var duration = originalEnd - originalStart;
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(7)) duration = TimeSpan.FromHours(1);
        return (startsAt, startsAt.Add(duration));
    }

    private static DateOnly? ResolveRelativeDate(string text, DateTimeOffset localNow)
    {
        var today = DateOnly.FromDateTime(localNow.Date);
        if (text.Contains("day after tomorrow", StringComparison.Ordinal) ||
            text.Contains("μεθαυριο", StringComparison.Ordinal)) return today.AddDays(2);
        if (text.Contains("tomorrow", StringComparison.Ordinal) ||
            text.Contains("αυριο", StringComparison.Ordinal)) return today.AddDays(1);
        if (text.Contains("today", StringComparison.Ordinal) ||
            text.Contains("σημερα", StringComparison.Ordinal)) return today;

        foreach (var (name, day) in Weekdays)
        {
            if (!WholeWord(name).IsMatch(text)) continue;
            var daysAhead = ((int)day - (int)localNow.DayOfWeek + 7) % 7;
            return today.AddDays(daysAhead == 0 ? 7 : daysAhead);
        }
        return null;
    }

    private static TimeOnly? ResolveTime(string text)
    {
        var match = TwentyFourHourTime().Match(text);
        if (match.Success)
            return new TimeOnly(Parse(match, "hour"), Parse(match, "minute"));

        match = EnglishTime().Match(text);
        if (match.Success)
        {
            var hour = Parse(match, "hour") % 12;
            if (match.Groups["period"].Value.StartsWith('p')) hour += 12;
            return new TimeOnly(hour, ParseOptional(match, "minute"));
        }

        match = GreekTime().Match(text);
        if (match.Success)
        {
            var hour = Parse(match, "hour");
            var period = match.Groups["period"].Value.Replace(".", string.Empty, StringComparison.Ordinal);
            if ((period is "απογευμα" or "βραδυ" or "μμ") && hour < 12) hour += 12;
            if (period == "πμ" && hour == 12) hour = 0;
            return new TimeOnly(hour, ParseOptional(match, "minute"));
        }

        if (text.Contains("morning", StringComparison.Ordinal) || text.Contains("πρωι", StringComparison.Ordinal))
            return new TimeOnly(9, 0);
        if (text.Contains("noon", StringComparison.Ordinal) || text.Contains("μεσημερι", StringComparison.Ordinal))
            return new TimeOnly(12, 0);
        if (text.Contains("afternoon", StringComparison.Ordinal) || text.Contains("απογευμα", StringComparison.Ordinal))
            return new TimeOnly(15, 0);
        if (text.Contains("evening", StringComparison.Ordinal) || text.Contains("βραδυ", StringComparison.Ordinal))
            return new TimeOnly(20, 0);
        return null;
    }

    private static int Parse(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
    private static int ParseOptional(Match match, string group) => match.Groups[group].Success ? Parse(match, group) : 0;

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static Regex WholeWord(string value) => new($@"\b{Regex.Escape(value)}\b", RegexOptions.CultureInvariant);

    [GeneratedRegex(@"(?<!\d)(?<hour>[01]?\d|2[0-3])[:.](?<minute>[0-5]\d)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex TwentyFourHourTime();

    [GeneratedRegex(@"\b(?:at\s*)?(?<hour>1[0-2]|0?[1-9])(?::(?<minute>[0-5]\d))?\s*(?<period>a\.?m\.?|p\.?m\.?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishTime();

    [GeneratedRegex(@"\bστις\s+(?<hour>1[0-2]|0?[1-9])(?::(?<minute>[0-5]\d))?(?:\s*(?:το|τη|την)?\s*(?<period>πρωι|μεσημερι|απογευμα|βραδυ|π\.?μ\.?|μ\.?μ\.?))?", RegexOptions.CultureInvariant)]
    private static partial Regex GreekTime();

    [GeneratedRegex(@"\b(?:κλεισε|προσθεσε|βαλε|δημιουργησε|προγραμματισε)\b.{0,80}\b(?:ραντεβου|συναντηση|γεγονος|καφε)\b|\b(?:ραντεβου|συναντηση|γεγονος|καφε)\b.{0,80}\b(?:αυριο|σημερα|μεθαυριο|δευτερα|τριτη|τεταρτη|πεμπτη|παρασκευη|σαββατο|κυριακη)\b", RegexOptions.CultureInvariant)]
    private static partial Regex GreekCreateIntent();

    [GeneratedRegex(@"\b(?:book|schedule|add|create|plan)\b.{0,80}\b(?:appointment|meeting|event|coffee)\b", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishCreateIntent();

    [GeneratedRegex(@"[\u0370-\u03ff]", RegexOptions.CultureInvariant)]
    private static partial Regex GreekCharacters();

    [GeneratedRegex(@"(?:ραντεβού|συνάντηση)\s+με\s+(?<subject>.+?)(?=\s+(?:σήμερα|αύριο|μεθαύριο|τη|την|στις|στις|για)\b|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GreekSubject();

    [GeneratedRegex(@"(?:appointment|meeting)\s+with\s+(?<subject>.+?)(?=\s+(?:today|tomorrow|next|on|at|for)\b|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnglishSubject();

    [GeneratedRegex(@"(?:καφέ|καφε|coffee)\s+(?:με|with)\s+(?:την|τον|τη|το)?\s*(?<subject>.+?)(?=\s+(?:σήμερα|αύριο|μεθαύριο|today|tomorrow|next|on|at|στις|για)\b|[.]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CoffeeSubject();

    [GeneratedRegex(@"\b(?:today|tomorrow|next|monday|tuesday|wednesday|thursday|friday|saturday|sunday|σημερα|αυριο|μεθαυριο|δευτερα|τριτη|τεταρτη|πεμπτη|παρασκευη|σαββατο|κυριακη|στις|at)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TemporalTitleWords();
}

public sealed record TemporalNormalizationResult(
    ParsedCommand? Command,
    string? Clarification,
    bool NeedsMoreInformation = false);
