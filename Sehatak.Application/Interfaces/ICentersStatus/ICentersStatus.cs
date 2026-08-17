using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.CentersStatusDto
{
    public interface ICentersStatus
    {
        Task<bool> SuspendedCenter(int centerId);
        Task<bool> ActiveCenter(int centerId);
    }
}
