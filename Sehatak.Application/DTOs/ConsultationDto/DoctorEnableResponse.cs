using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.ConsultationDto
{
    public class DoctorEnableResponse
    {
        public int doctorId {  get; set; }
        public string doctorName {  get; set; }
        public string? profileImage { get; set; }
        public string depatrmentName { get; set; }
        public string Bio {  get; set; }
        public string Specialization { get; set; }
    }
}
