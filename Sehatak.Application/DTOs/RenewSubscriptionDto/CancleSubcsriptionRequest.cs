using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.RenewSubscription
{
    public class CancleSubcsriptionRequest
    {
        public int subscriptionId { get; set; }
        public int userId { get; set; }
    }
}
