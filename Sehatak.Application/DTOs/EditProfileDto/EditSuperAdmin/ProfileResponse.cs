using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.EditProfile.EditSuperAdmin
{
    public class ProfileResponse
    {
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string? ProfileImage { get; set; }
    }
}
