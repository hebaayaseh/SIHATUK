using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.SubPatientDto
{
    public class SummarySubPatientResponseDto
    {
        public int Id { get; set; }
        public string SubPatientFirstName { get; set; } = null!;
        public string SubPatientLastName { get; set; } = null!;
        public BloodType BloodType { get; set; }
        public string WhatAppNumber { get; set; } = null!;
        public Gender Gender { get; set; }
        public DateOnly DateOfBith { get; set; }
    }
}
