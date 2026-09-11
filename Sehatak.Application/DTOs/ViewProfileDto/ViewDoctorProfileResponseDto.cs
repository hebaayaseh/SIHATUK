using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.ViewProfileDto
{
    public class ViewDoctorProfileResponseDto
    {
        public int Id {  get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Specialization { get; set; }
        public string Bio { get; set; }
        public bool OnlineEnabled { get; set; } = false;
        public string? PhoneNumber {  get; set; }
        public string Email { get; set; }
        public string? ProfileImage { get; set; }
        public string city { get; set; }
        public string Address {  get; set; }
    }
}
