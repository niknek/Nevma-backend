using Nevma.Planning.Api.Domain.Calendar;

namespace Nevma.Planning.Tests;

public sealed class PersonalCalendarEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Group_appointment_stays_pending_until_everyone_accepts()
    {
        var organizer = Guid.NewGuid();
        var maria = Guid.NewGuid();
        var nikos = Guid.NewGuid();
        var appointment = PersonalCalendarEvent.Create(
            organizer, "Team meeting", null, null, Now.AddDays(1), Now.AddDays(1).AddHours(1),
            [maria, nikos, maria, organizer], Now);

        Assert.Equal(PersonalCalendarEventStatus.Pending, appointment.Status);
        Assert.Equal(2, appointment.ParticipantIds.Length);

        Assert.Equal(ParticipantResponseOutcome.Updated, appointment.Accept(maria, Now.AddMinutes(1)));
        Assert.Equal(PersonalCalendarEventStatus.Pending, appointment.Status);
        Assert.Equal(ParticipantResponseOutcome.Updated, appointment.Accept(nikos, Now.AddMinutes(2)));
        Assert.Equal(PersonalCalendarEventStatus.Confirmed, appointment.Status);
    }

    [Fact]
    public void A_decline_marks_the_group_appointment_as_declined()
    {
        var participant = Guid.NewGuid();
        var appointment = PersonalCalendarEvent.Create(
            Guid.NewGuid(), "Project review", null, null, Now.AddDays(1), Now.AddDays(1).AddHours(1),
            [participant], Now);

        var outcome = appointment.Decline(participant, Now.AddMinutes(1));

        Assert.Equal(ParticipantResponseOutcome.Updated, outcome);
        Assert.Equal(PersonalCalendarEventStatus.Declined, appointment.Status);
    }
}
