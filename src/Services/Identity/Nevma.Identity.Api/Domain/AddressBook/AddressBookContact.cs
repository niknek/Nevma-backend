namespace Nevma.Identity.Api.Domain.AddressBook;

public sealed class AddressBookContact
{
    private AddressBookContact() { }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid? LinkedUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Social { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AddressBookContact Create(
        Guid ownerId,
        string name,
        string phone,
        string? email,
        string? social,
        string? address,
        string? notes,
        Guid? linkedUserId,
        DateTimeOffset now)
    {
        var contact = new AddressBookContact
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            CreatedAt = now.ToUniversalTime()
        };
        contact.Update(name, phone, email, social, address, notes, linkedUserId, now);
        return contact;
    }

    public void Update(
        string name,
        string phone,
        string? email,
        string? social,
        string? address,
        string? notes,
        Guid? linkedUserId,
        DateTimeOffset now)
    {
        Name = Required(name, 150, nameof(name));
        Phone = NormalizePhone(phone);
        Email = Optional(email, 320, nameof(email));
        if (Email is not null && !System.Net.Mail.MailAddress.TryCreate(Email, out _))
            throw new ArgumentException("Email is not valid.", nameof(email));
        Social = Optional(social, 500, nameof(social));
        Address = Optional(address, 500, nameof(address));
        Notes = Optional(notes, 1_000, nameof(notes));
        LinkedUserId = linkedUserId == OwnerId ? null : linkedUserId;
        UpdatedAt = now.ToUniversalTime();
    }

    public static string NormalizePhone(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Phone is required.", nameof(value));
        var trimmed = value.Trim();
        var prefix = trimmed.StartsWith('+') ? "+" : string.Empty;
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length is < 7 or > 15) throw new ArgumentException("Phone must contain 7 to 15 digits.", nameof(value));
        return prefix + digits;
    }

    private static string Required(string value, int max, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Name is required.", parameter);
        var result = value.Trim();
        if (result.Length > max) throw new ArgumentException($"Value cannot exceed {max} characters.", parameter);
        return result;
    }

    private static string? Optional(string? value, int max, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim();
        if (result.Length > max) throw new ArgumentException($"Value cannot exceed {max} characters.", parameter);
        return result;
    }
}
