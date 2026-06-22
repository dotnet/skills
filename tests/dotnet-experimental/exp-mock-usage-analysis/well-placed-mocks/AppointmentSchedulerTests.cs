using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Scheduling;

namespace Scheduling.Tests;

[TestClass]
public sealed class AppointmentSchedulerTests
{
    [TestMethod]
    public void ScheduleAppointment_AvailableSlot_BooksAndSendsConfirmation()
    {
        var mockCalendar = new Mock<ICalendarService>();
        var mockEmail = new Mock<IEmailService>();

        var slots = new List<TimeSlot>
        {
            new("slot-1", new DateTime(2025, 3, 15, 9, 0, 0), new DateTime(2025, 3, 15, 10, 0, 0), true)
        };
        mockCalendar.Setup(c => c.GetAvailableSlots(It.IsAny<DateTime>())).Returns(slots);
        mockCalendar.Setup(c => c.BookSlot("slot-1", "user@example.com")).Returns(true);

        var scheduler = new AppointmentScheduler(mockCalendar.Object, mockEmail.Object);

        var result = scheduler.ScheduleAppointment(new DateTime(2025, 3, 15), "user@example.com");

        Assert.IsTrue(result);
        mockEmail.Verify(e => e.SendConfirmation("user@example.com", "Appointment Confirmed", It.IsAny<string>()), Times.Once);
    }

    [TestMethod]
    public void ScheduleAppointment_NoAvailableSlots_ReturnsFalse()
    {
        var mockCalendar = new Mock<ICalendarService>();
        var mockEmail = new Mock<IEmailService>();

        mockCalendar.Setup(c => c.GetAvailableSlots(It.IsAny<DateTime>())).Returns(new List<TimeSlot>());

        var scheduler = new AppointmentScheduler(mockCalendar.Object, mockEmail.Object);

        var result = scheduler.ScheduleAppointment(new DateTime(2025, 3, 15), "user@example.com");

        Assert.IsFalse(result);
        mockEmail.Verify(e => e.SendConfirmation(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void ScheduleAppointment_BookingFails_DoesNotSendEmail()
    {
        var mockCalendar = new Mock<ICalendarService>();
        var mockEmail = new Mock<IEmailService>();

        var slots = new List<TimeSlot>
        {
            new("slot-2", new DateTime(2025, 3, 15, 14, 0, 0), new DateTime(2025, 3, 15, 15, 0, 0), true)
        };
        mockCalendar.Setup(c => c.GetAvailableSlots(It.IsAny<DateTime>())).Returns(slots);
        mockCalendar.Setup(c => c.BookSlot("slot-2", "user@example.com")).Returns(false);

        var scheduler = new AppointmentScheduler(mockCalendar.Object, mockEmail.Object);

        var result = scheduler.ScheduleAppointment(new DateTime(2025, 3, 15), "user@example.com");

        Assert.IsFalse(result);
        mockEmail.Verify(e => e.SendConfirmation(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
