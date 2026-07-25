using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.PatientCenter
{
    public class GetPatientResponseDto
    {
        public int Id { get; set; }
        public string pateintName { get; set; }
        public List<PatientSummaryDto> appointments { get; set; } 
    }
}
