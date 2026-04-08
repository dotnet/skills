namespace Scheduling;

public interface ICalendarService
{
    IReadOnlyList<TimeSlot> GetAvailableSlots(DateTime date);
    bool BookSlot(string slotId, string attendeeEmail);
}

public interface IEmailService
{
    void SendConfirmation(string to, string subject, string body);
}

public record TimeSlot(string Id, DateTime Start, DateTime End, bool IsAvailable);

public class AppointmentScheduler
{
    private readonly ICalendarService _calendar;
    private readonly IEmailService _email;

    public AppointmentScheduler(ICalendarService calendar, IEmailService email)
    {
        _calendar = calendar;
        _email = email;
    }

    public bool ScheduleAppointment(DateTime date, string attendeeEmail)
    {
        var slots = _calendar.GetAvailableSlots(date);
        var firstAvailable = slots.FirstOrDefault(s => s.IsAvailable);
        if (firstAvailable is null)
            return false;

        var booked = _calendar.BookSlot(firstAvailable.Id, attendeeEmail);
        if (booked)
        {
            _email.SendConfirmation(
                attendeeEmail,
                "Appointment Confirmed",
                $"Your appointment on {firstAvailable.Start:g} is confirmed.");
        }

        return booked;
    }
}
