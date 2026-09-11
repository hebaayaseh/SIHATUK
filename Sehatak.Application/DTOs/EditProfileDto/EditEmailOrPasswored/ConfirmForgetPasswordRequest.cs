using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.EditProfileDto.EditEmailOrPasswored
{
    public class ConfirmForgetPasswordRequest
    {
        public string Email { get; set; }
        public string Code { get; set; }
    }
}
