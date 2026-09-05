using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.SubPatientDto
{
    public class UpdateSubPatientRequestDto
    {
        public string? SubPatientFirstName { get; set; } = null!;
        public string? SubPatientLastName { get; set; } = null!;
        public BloodType? BloodType { get; set; } = null;
        public string? WhatAppNumber { get; set; } = null!;
        public Gender? Gender { get; set; } = null;
        public DateOnly? DateOfBith { get; set; } = null;
    }
}
