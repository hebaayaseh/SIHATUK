using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.Auth
{
    public class LogoutRequestDto
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
}
