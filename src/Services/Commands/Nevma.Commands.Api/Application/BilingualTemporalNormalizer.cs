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
    private static readonly IReadOnlyDictionary<string, int> SpokenHours = new Dictionary<string, int>
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
        ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16,
        ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20,
        ["twenty one"] = 21, ["twenty two"] = 22, ["twenty three"] = 23,
        ["μια"] = 1, ["ενα"] = 1, ["δυο"] = 2, ["τρεις"] = 3, ["τρια"] = 3,
        ["τεσσερις"] = 4, ["τεσσερα"] = 4, ["πεντε"] = 5, ["εξι"] = 6,
        ["επτα"] = 7, ["εφτα"] = 7, ["οκτω"] = 8, ["οχτω"] = 8, ["εννια"] = 9,
        ["δεκα"] = 10, ["εντεκα"] = 11, ["δωδεκα"] = 12, ["δεκατρεις"] = 13,
        ["δεκατεσσερις"] = 14, ["δεκατεσσερα"] = 14, ["δεκαπεντε"] = 15, ["δεκαεξι"] = 16,
        ["δεκαεπτα"] = 17, ["δεκαεφτα"] = 17, ["δεκαοκτω"] = 18, ["δεκαοχτω"] = 18,
        ["δεκαεννια"] = 19, ["εικοσι"] = 20, ["εικοσι μια"] = 21, ["εικοσι ενα"] = 21,
        ["εικοσι δυο"] = 22, ["εικοσι τρεις"] = 23, ["εικοσι τρια"] = 23
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
        var isGreek = IsGreekMessage(message, detectedLanguage);
        var relativeDate = ResolveRelativeDate(normalized, localNow);
        var explicitTime = ResolveTime(normalized);
        if (relativeDate is not null && explicitTime is null)
            return new TemporalNormalizationResult(null, isGreek
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
        var isGreek = IsGreekMessage(message, detectedLanguage);
        var isGreekRequest = GreekCreateIntent().IsMatch(normalized);
        var isEnglishRequest = EnglishCreateIntent().IsMatch(normalized);
        if (!isGreekRequest && !isEnglishRequest)
            return new TemporalNormalizationResult(null, null);

        var date = ResolveRelativeDate(normalized, localNow);
        var time = ResolveTime(normalized);
        if (date is null || time is null)
        {
            var clarification = isGreek
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
            return new TemporalNormalizationResult(null, isGreek
                ? $"Η επαφή {names} δεν έχει ακόμη συνδεδεμένο λογαριασμό Nevma. Για να λάβει notification και να αποδεχτεί το ραντεβού, πρέπει να εγγραφεί με το ίδιο email ή τηλέφωνο."
                : $"{names} is not linked to a Nevma account yet. They must register with the same email or phone to receive and accept the invitation.");
        }

        var title = ChooseTitle(null, message, detectedLanguage);
        var startsAt = new DateTimeOffset(date.Value.ToDateTime(time.Value), localNow.Offset);
        var arguments = new CreateCalendarEventCommandArguments(
            title, message.Trim(), ResolveLocation(message), startsAt, startsAt.AddHours(1), ResolveParticipantIds(message, contacts));
        var command = new ParsedCommand(
            CommandIntent.CreateCalendarEvent,
            isGreek ? $"Δημιουργία: {title}" : $"Create: {title}",
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
        var isGreek = IsGreekMessage(message, detectedLanguage);
        var unlinkedContacts = ResolveMentionedUnlinkedContacts(message, contacts);
        if (unlinkedContacts.Count > 0)
        {
            var names = string.Join(", ", unlinkedContacts);
            return new TemporalNormalizationResult(null, isGreek
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
            Location = ResolveLocation(message) ?? request.Location,
            StartsAt = startsAt,
            EndsAt = endsAt,
            ParticipantIds = participants.Count > 0 ? participants : request.ParticipantIds
        }, JsonOptions);
        var summary = isGreek ? $"Δημιουργία: {title}" : $"Create: {title}";
        return new TemporalNormalizationResult(command with { Summary = summary, ArgumentsJson = json }, null);
    }

    private static string ChooseTitle(string? proposedTitle, string message, string detectedLanguage)
    {
        var normalized = RemoveDiacritics(message).ToLowerInvariant();
        var isGreek = IsGreekMessage(message, detectedLanguage);

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
            return ApplySpokenPeriod(Parse(match, "hour"), Parse(match, "minute"), match.Groups["period"].Value);

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
            if ((period is "απογευμα" or "πογευμα" or "βραδυ" or "μμ") && hour < 12) hour += 12;
            if (period == "πμ" && hour == 12) hour = 0;
            return new TimeOnly(hour, ParseOptional(match, "minute"));
        }

        match = EnglishHalfPastTime().Match(text);
        if (match.Success)
            return ResolveRelativeSpokenTime(match, 30);

        match = EnglishRelativeTime().Match(text);
        if (match.Success)
            return ResolveRelativeSpokenTime(match, SpokenMinute(match.Groups["minute"].Value));

        match = EnglishSpokenTime().Match(text);
        if (match.Success)
        {
            var minute = SpokenMinute(match.Groups["minute"].Value);
            return ApplySpokenPeriod(SpokenHours[match.Groups["hourword"].Value], minute, match.Groups["period"].Value);
        }

        match = GreekSpokenTime().Match(text);
        if (match.Success)
        {
            var minute = SpokenMinute(match.Groups["minute"].Value);
            var hour = SpokenHours[match.Groups["hourword"].Value];
            if (match.Groups["before"].Success)
            {
                hour = (hour + 23) % 24;
                minute = 60 - minute;
            }
            return ApplySpokenPeriod(hour, minute, match.Groups["period"].Value);
        }

        if (text.Contains("morning", StringComparison.Ordinal) || text.Contains("πρωι", StringComparison.Ordinal))
            return new TimeOnly(9, 0);
        if (text.Contains("noon", StringComparison.Ordinal) || text.Contains("μεσημερι", StringComparison.Ordinal))
            return new TimeOnly(12, 0);
        if (text.Contains("afternoon", StringComparison.Ordinal) || text.Contains("απογευμα", StringComparison.Ordinal) || text.Contains("πογευμα", StringComparison.Ordinal))
            return new TimeOnly(15, 0);
        if (text.Contains("evening", StringComparison.Ordinal) || text.Contains("βραδυ", StringComparison.Ordinal))
            return new TimeOnly(20, 0);
        return null;
    }

    private static TimeOnly ApplySpokenPeriod(int hour, int minute, string period)
    {
        var normalizedPeriod = period.Replace(".", string.Empty, StringComparison.Ordinal);
        if ((normalizedPeriod is "pm" or "afternoon" or "evening" or "απογευμα" or "πογευμα" or "βραδυ" or "μμ") && hour < 12)
            hour += 12;
        if ((normalizedPeriod is "am" or "morning" or "πρωι" or "πμ") && hour == 12)
            hour = 0;
        return new TimeOnly(hour, minute);
    }

    private static TimeOnly ResolveRelativeSpokenTime(Match match, int minute)
    {
        var hour = SpokenHours[match.Groups["hourword"].Value];
        if (match.Groups["before"].Success)
        {
            hour = (hour + 23) % 24;
            minute = 60 - minute;
        }
        return ApplySpokenPeriod(hour, minute, match.Groups["period"].Value);
    }

    private static int SpokenMinute(string value) => value.Trim() switch
    {
        "five" or "πεντε" => 5,
        "ten" or "δεκα" => 10,
        "quarter" or "fifteen" or "τεταρτο" or "δεκαπεντε" => 15,
        "twenty" or "εικοσι" => 20,
        "twenty five" or "εικοσι πεντε" => 25,
        "half" or "thirty" or "μιση" or "μισι" or "τριαντα" => 30,
        "thirty five" or "τριαντα πεντε" => 35,
        "forty" or "σαραντα" => 40,
        "forty five" or "σαραντα πεντε" => 45,
        "fifty" or "πενηντα" => 50,
        "fifty five" or "πενηντα πεντε" => 55,
        _ => 0
    };

    private static int Parse(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
    private static int ParseOptional(Match match, string group) => match.Groups[group].Success ? Parse(match, group) : 0;

    private static string? ResolveLocation(string message)
    {
        var matches = LocationTail().Matches(message);
        if (matches.Count == 0) return null;
        var value = matches[^1].Groups["location"].Value.Trim(' ', '.', ',', ';', ':');
        return value.Length is > 1 and <= 300 ? value : null;
    }

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsGreekMessage(string message, string detectedLanguage) =>
        detectedLanguage.StartsWith("el", StringComparison.OrdinalIgnoreCase);

    private static Regex WholeWord(string value) => new($@"\b{Regex.Escape(value)}\b", RegexOptions.CultureInvariant);

    [GeneratedRegex(@"(?<!\d)(?<hour>[01]?\d|2[0-3])[:.](?<minute>[0-5]\d)(?!\d)(?:\s*(?:in\s+the\s+|το\s+)?(?<period>morning|afternoon|evening|a\.?m\.?|p\.?m\.?|πρωι|μεσημερι|απογευμα|βραδυ|π\.?μ\.?|μ\.?μ\.?))?", RegexOptions.CultureInvariant)]
    private static partial Regex TwentyFourHourTime();

    [GeneratedRegex(@"\b(?:at\s*)?(?<hour>1[0-2]|0?[1-9])(?::(?<minute>[0-5]\d))?\s*(?<period>a\.?m\.?|p\.?m\.?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishTime();

    [GeneratedRegex(@"\bστις\s+(?<hour>1[0-2]|0?[1-9])(?::(?<minute>[0-5]\d))?(?:\s*ωρα)?(?:\s*(?:το|τη|την|τα)?\s*(?<period>πρωι|μεσημερι|απογευμα|πογευμα|βραδυ|π\.?μ\.?|μ\.?μ\.?))?", RegexOptions.CultureInvariant)]
    private static partial Regex GreekTime();

    [GeneratedRegex(@"\b(?<minute>half)\s+(?:(?<before>to)|past)\s+(?<hourword>one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)(?:\s+(?:in\s+the\s+)?(?<period>morning|afternoon|evening|a\.?m\.?|p\.?m\.?))?\b", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishHalfPastTime();

    [GeneratedRegex(@"\b(?<minute>five|ten|quarter|fifteen|twenty|twenty\s+five)\s+(?:(?<before>to)|past)\s+(?<hourword>one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)(?:\s+(?:in\s+the\s+)?(?<period>morning|afternoon|evening|a\.?m\.?|p\.?m\.?))?\b", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishRelativeTime();

    [GeneratedRegex(@"\bat\s+(?<hourword>twenty\s+(?:one|two|three)|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)(?:\s+(?<minute>five|ten|quarter|fifteen|twenty(?:\s+five)?|half|thirty(?:\s+five)?|forty(?:\s+five)?|fifty(?:\s+five)?))?(?:\s+(?:in\s+the\s+)?(?<period>morning|afternoon|evening|a\.?m\.?|p\.?m\.?))?\b", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishSpokenTime();

    [GeneratedRegex(@"\bστις\s+(?<hourword>εικοσι\s+(?:μια|ενα|δυο|τρεις|τρια)|δεκα(?:τρεις|τεσσερις|τεσσερα|πεντε|εξι|επτα|εφτα|οκτω|οχτω|εννια)|μια|ενα|δυο|τρεις|τρια|τεσσερις|τεσσερα|πεντε|εξι|επτα|εφτα|οκτω|οχτω|εννια|δεκα|εντεκα|δωδεκα|εικοσι)(?:(?:\s+και\s*|\s*)(?<minute>πεντε|δεκα|τεταρτο|δεκαπεντε|εικοσι(?:\s+πεντε)?|μιση|μισι|τριαντα(?:\s+πεντε)?|σαραντα(?:\s+πεντε)?|πενηντα(?:\s+πεντε)?)|\s+(?<before>παρα)\s+(?<minute>πεντε|δεκα|τεταρτο|δεκαπεντε|εικοσι(?:\s+πεντε)?))?(?:\s+ωρα)?(?:\s+(?:το|τη|την|τα)?\s*(?<period>πρωι|μεσημερι|απογευμα|πογευμα|βραδυ|π\.?μ\.?|μ\.?μ\.?))?\b", RegexOptions.CultureInvariant)]
    private static partial Regex GreekSpokenTime();

    [GeneratedRegex(@"\b(?:κλεισε|προσθεσε|βαλε|δημιουργησε|προγραμματισε)\b.{0,80}\b(?:ραντεβου|συναντηση|γεγονος|καφε)\b|\bθελω\s+να\s+(?:κλεισω|κανονισω|κανω)\b.{0,100}\b(?:ραντεβου|συναντηση|γεγονος|καφε)\b|\b(?:ραντεβου|συναντηση|γεγονος|καφε)\b.{0,80}\b(?:αυριο|σημερα|μεθαυριο|δευτερα|τριτη|τεταρτη|πεμπτη|παρασκευη|σαββατο|κυριακη)\b", RegexOptions.CultureInvariant)]
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

    [GeneratedRegex(@"\b(?:στο|στη|στην|at)\s+(?<location>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationTail();

    [GeneratedRegex(@"\b(?:today|tomorrow|next|monday|tuesday|wednesday|thursday|friday|saturday|sunday|σημερα|αυριο|μεθαυριο|δευτερα|τριτη|τεταρτη|πεμπτη|παρασκευη|σαββατο|κυριακη|στις|at)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TemporalTitleWords();
}

public sealed record TemporalNormalizationResult(
    ParsedCommand? Command,
    string? Clarification,
    bool NeedsMoreInformation = false);
