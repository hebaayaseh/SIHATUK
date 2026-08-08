using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditProfileActors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.IProfileInterface
{
    public interface IProfilePatient
    {
        Task<EditPatientInformationResponse> EditPatientInformation(int centerId , int userId,EditPatientInformationRequest request);
        Task<EditPatientInformationResponse> ViewPatientInformation(int centerId , int userId);
        Task<bool> RequestEditEmail(int centerId , int userId, EditEmailRequest request);
        Task<EmailResponse> ConfirmEditEmail(int centerId , int userId, ConfirmEditEmailRequest request);

        Task<bool> RequestEditPassword(int centerId , int userId, EditPasswordRequest request);
        Task<PasswordResponse> ConfirmEditPassword(int centerId , int userId, ConfirmEditPasswordRequest request);
    }
}
