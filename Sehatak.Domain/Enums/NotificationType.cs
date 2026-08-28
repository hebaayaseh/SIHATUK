using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Domain.Enums
{
    public enum NotificationType
    {
        Appointment = 1,
        Cancellation = 2,
        Reminder = 3,
        LabResult = 4,
        System = 5,
        Waitlist = 6,
        Shift = 7
    }
}
