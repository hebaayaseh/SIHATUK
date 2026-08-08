using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditSuperAdmin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.IProfileInterface
{
    public interface IProfile
    {
        Task<ProfileResponse> ViewProfile(int UserId);

        Task<bool> RequestEditEmail(int userId, EditEmailRequest request);
        Task<EmailResponse> ConfirmEditEmail(int userId, ConfirmEditEmailRequest request);

        Task<bool> RequestEditPassword(int userId, EditPasswordRequest request);
        Task<PasswordResponse> ConfirmEditPassword(int userId, ConfirmEditPasswordRequest request);

        Task<NameResponse> EditName(int userId, EditNameRequest request);
        Task<ProfileImageResponse> EditProfileImage(int userId, EditProfileImageRequest request);
    }
}
