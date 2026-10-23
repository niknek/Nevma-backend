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

        var outcome = appointment.Decline(participant, "Cannot attend", Now.AddMinutes(1));

        Assert.Equal(ParticipantResponseOutcome.Updated, outcome);
        Assert.Equal(PersonalCalendarEventStatus.Declined, appointment.Status);
        Assert.Equal("Cannot attend", appointment.DeclinedParticipantReasons.Single());
    }

    [Fact]
    public void One_decline_does_not_cancel_a_group_appointment_with_an_acceptance()
    {
        var maria = Guid.NewGuid();
        var nikos = Guid.NewGuid();
        var appointment = PersonalCalendarEvent.Create(
            Guid.NewGuid(), "Dinner", null, null, Now.AddDays(1), Now.AddDays(1).AddHours(1),
            [maria, nikos], Now);

        appointment.Accept(maria, Now.AddMinutes(1));
        appointment.Decline(nikos, "Away", Now.AddMinutes(2));

        Assert.Equal(PersonalCalendarEventStatus.PartiallyAccepted, appointment.Status);
        Assert.Contains(maria, appointment.AcceptedParticipantIds);
        Assert.Contains(nikos, appointment.DeclinedParticipantIds);
    }

    [Fact]
    public void A_pending_appointment_is_cancelled_after_its_end_time()
    {
        var appointment = PersonalCalendarEvent.Create(
            Guid.NewGuid(), "Coffee", null, null, Now.AddMinutes(10), Now.AddMinutes(40),
            [Guid.NewGuid()], Now);

        Assert.False(appointment.CancelIfExpired(Now.AddMinutes(9)));
        Assert.True(appointment.CancelIfExpired(Now.AddMinutes(10)));
        Assert.Equal(PersonalCalendarEventStatus.Cancelled, appointment.Status);
    }

    [Fact]
    public void A_future_appointment_can_be_cancelled_with_a_reason()
    {
        var appointment = PersonalCalendarEvent.Create(
            Guid.NewGuid(), "Coffee", null, null, Now.AddHours(1), Now.AddHours(2),
            [Guid.NewGuid()], Now);

        var outcome = appointment.Cancel("Plans changed", Now.AddMinutes(1));

        Assert.Equal(ManualCancellationOutcome.Updated, outcome);
        Assert.Equal(PersonalCalendarEventStatus.Cancelled, appointment.Status);
        Assert.Equal("Plans changed", appointment.CancellationReason);
    }

    [Fact]
    public void A_started_appointment_cannot_be_cancelled_manually()
    {
        var appointment = PersonalCalendarEvent.Create(
            Guid.NewGuid(), "Coffee", null, null, Now.AddMinutes(10), Now.AddHours(1),
            null, Now);

        Assert.Equal(ManualCancellationOutcome.AppointmentStarted, appointment.Cancel("Too late", Now.AddMinutes(10)));
    }

    [Fact]
    public void Live_location_consent_is_independent_for_each_participant()
    {
        var organizer = Guid.NewGuid();
        var participant = Guid.NewGuid();
        var appointment = PersonalCalendarEvent.Create(
            organizer, "Coffee", null, null, Now.AddHours(1), Now.AddHours(2),
            [participant], true, Now);

        Assert.Contains(organizer, appointment.LiveLocationSharingUserIds);
        Assert.DoesNotContain(participant, appointment.LiveLocationSharingUserIds);

        Assert.True(appointment.SetLiveLocationSharing(participant, true, Now.AddMinutes(1)));
        Assert.Contains(participant, appointment.LiveLocationSharingUserIds);

        Assert.True(appointment.SetLiveLocationSharing(organizer, false, Now.AddMinutes(2)));
        Assert.DoesNotContain(organizer, appointment.LiveLocationSharingUserIds);
        Assert.Contains(participant, appointment.LiveLocationSharingUserIds);
    }
}
