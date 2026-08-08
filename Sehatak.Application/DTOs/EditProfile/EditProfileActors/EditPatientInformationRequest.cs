using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.EditProfile.EditProfileActors
{
    public class EditPatientInformationRequest
    {
        public string? firstNmae { get; set; }
        public string? lastNmae { get; set; }
        public IFormFile? profileImage { get; set; }
        public bool RemoveProfileImage { get; set; } = false;
        public string? address { get; set; }
        public string? city { get; set; }
        public string? phoneNumber { get; set; }
        
    }
}
