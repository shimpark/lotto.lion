﻿using LottoLion.BaseLib.Controllers;
using LottoLion.BaseLib.Models.Entity;
using LottoLion.BaseLib.Options;
using LottoLion.BaseLib.Queues;
using LottoLion.BaseLib.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OdinSdk.BaseLib.Configuration;
using OdinSdk.BaseLib.Cryption;
using OdinSdk.BaseLib.WebApi;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace LottoLion.WebApi.Controllers
{
    [Route("api/[controller]")]
    public partial class UserController : Controller
    {
        private static CConfig __cconfig = new CConfig();
        private static CCryption __cryptor = new CCryption();
        private static MemberQ __memberQ;

        private UserManager __usermgr;
        private LottoLionContext __db_context;

        public UserController(IOptions<JwtIssuerOptions> jwtOptions, IConfigurationRoot config_root, LottoLionContext db_context)
        {
            __usermgr = new UserManager(jwtOptions.Value);
            __cconfig.SetConfigRoot(config_root);

            __db_context = db_context;
            __memberQ = new MemberQ();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [Route("GetUserInfor")]
        [Authorize(Policy = "LottoLionMember")]
        [HttpPost]
        public async Task<IActionResult> GetUserInfor()
        {
            return await CProxy.Using(() =>
            {
                var _result = (success: false, message: "ok");

                var _user_infor = (TbLionMember)null;

                while (true)
                {
                    var _login_id = __usermgr.GetLoginId(Request);
                    if (String.IsNullOrEmpty(_login_id) == true)
                    {
                        _result.message = "인증 정보에서 회원ID를 찾을 수 없습니다";
                        break;
                    }

                    _user_infor = __db_context.TbLionMember
                                        .Where(m => m.LoginId == _login_id && m.IsAlive == true)
                                        .SingleOrDefault();

                    if (_user_infor == null)
                    {
                        _result.message = "회원님의 정보에 오류가 있습니다, 관리자에게 문의 하세요";
                        break;
                    }

                    _result.message = "회원 정보를 찾았습니다";
                    _result.success = true;

                    break;
                }

                return new OkObjectResult(new
                {
                    success = _result.success,
                    message = _result.message,

                    result = (_user_infor != null) ? new
                    {
                        loginId = _user_infor.LoginId,
                        loginName = _user_infor.LoginName,

                        phoneNumber = _user_infor.PhoneNumber,
                        emailAddress = _user_infor.EmailAddress,
                        mailError = _user_infor.MailError,
                        isMailSend = _user_infor.IsMailSend,

                        maxSelectNumber = _user_infor.MaxSelectNumber,

                        digit1 = _user_infor.Digit1,
                        digit2 = _user_infor.Digit2,
                        digit3 = _user_infor.Digit3
                    } : null
                });
            });
        }

        [Route("AddMemberByLoginId")]
        [Authorize(Policy = "LottoLionGuest")]
        [HttpPost]
        public async Task<IActionResult> AddMemberByLoginId([FromForm] ApplicationUser app_user)
        {
            return await CProxy.UsingAsync(async () =>
            {
                var _result = (success: false, message: "ok");

                while (true)
                {
                    if (String.IsNullOrEmpty(app_user.mail_address) == true)
                    {
                        _result.message = "메일 주소가 필요 합니다";
                        break;
                    }

                    var _value = "";

                    var _validate = __verify_email.TryRemove(app_user.mail_address, out _value);
                    if (_validate == false)
                    {
                        _result.message = $"검증 하고자 하는 메일 주소({app_user.mail_address})가 아닙니다";
                        break;
                    }

                    _validate = _value == app_user.check_number;
                    if (_validate == false)
                    {
                        _result.message = $"메일 검증 번호({app_user.check_number})가 일치 하지 않습니다";
                        break;
                    }

                    if (String.IsNullOrEmpty(app_user.login_id) == true
                        || String.IsNullOrEmpty(app_user.login_name) == true || String.IsNullOrEmpty(app_user.mail_address) == true
                        )
                    {
                        _result.message = "회원ID, 회원이름, 메일 주소가 필요 합니다";
                        break;
                    }

                    var _member = __db_context.TbLionMember
                                                .Where(m => m.EmailAddress == app_user.mail_address)
                                                .SingleOrDefault();

                    if (_member == null)
                    {
                        _member = __db_context.TbLionMember
                                                .Where(m => m.LoginId == app_user.login_id)
                                                .SingleOrDefault();

                        if (_member != null)
                        {
                            _result.message = $"동일한 회원ID({app_user.login_id})가 이미 사용 중 입니다";
                            break;
                        }

                        if (__usermgr.AddNewMember(__db_context, app_user) == false)
                        {
                            _result.message = $"모바일 장치 정보 오류 입니다";
                            break;
                        }

                        _result.message = $"신규 회원으로 등록 되었습니다";
                        _result.success = true;
                    }
                    else
                    {
                        if (_member.IsAlive == true)
                        {
                            _result.message = $"다른 회원이 이미 사용하고 있는 메일 주소({app_user.mail_address}) 입니다";
                            break;
                        }

                        if (__usermgr.ReUseMember(__db_context, _member, app_user) == false)
                        {
                            _result.message = $"모바일 장치 정보 오류 입니다";
                            break;
                        }

                        _result.message = $"기존 회원으로 등록 되었습니다";
                        _result.success = true;
                    }

                    // 가입 member(회원)에게 즉시 메일 발송 하도록 큐에 명령을 보냅니다.
                    if (_result.success == true)
                    {
                        var _choice = new TChoice()
                        {
                            login_id = app_user.login_id,
                            sequence_no = WinnerReader.GetNextWeekSequenceNo(),
                            resend = true
                        };

                        await __memberQ.SendQAsync(_choice);
                    }

                    break;
                }

                return new OkObjectResult(new
                {
                    success = _result.success,
                    message = _result.message,

                    result = ""
                });
            });
        }

        [Route("AddMemberByFacebook")]
        [Authorize(Policy = "LottoLionGuest")]
        [HttpPost]
        public async Task<IActionResult> AddMemberByFacebook([FromForm] ApplicationUser app_user)
        {
            return await CProxy.UsingAsync(async () =>
            {
                var _result = (success: false, message: "ok");

                while (true)
                {
                    if (String.IsNullOrEmpty(app_user.mail_address) == true)
                    {
                        _result.message = "메일 주소가 필요 합니다";
                        break;
                    }

                    var _value = "";

                    var _validate = __verify_email.TryRemove(app_user.mail_address, out _value);
                    if (_validate == false)
                    {
                        _result.message = $"검증 하고자 하는 메일 주소({app_user.mail_address})가 아닙니다";
                        break;
                    }

                    _validate = _value == app_user.check_number;
                    if (_validate == false)
                    {
                        _result.message = $"메일 검증 번호({app_user.check_number})가 일치 하지 않습니다";
                        break;
                    }

                    if (String.IsNullOrEmpty(app_user.login_name) == true
                        || String.IsNullOrEmpty(app_user.facebook_id) == true || String.IsNullOrEmpty(app_user.facebook_token) == true
                        )
                    {
                        _result.message = "facebook-id, facebook-token, 회원-이름이 필요 합니다";
                        break;
                    }

                    _validate = await __usermgr.VerifyFacebookToken(app_user.facebook_token, app_user.facebook_id);
                    if (_validate == false)
                    {
                        _result.message = $"facebook-token 또는 facebook-id({app_user.facebook_id}) 오류 입니다";
                        break;
                    }

                    app_user.login_id = app_user.facebook_id;

                    var _member = __db_context.TbLionMember
                                                .Where(m => m.EmailAddress == app_user.mail_address)
                                                .SingleOrDefault();

                    if (_member == null)
                    {
                        _member = __db_context.TbLionMember
                                                    .Where(m => m.LoginId == app_user.facebook_id)
                                                    .SingleOrDefault();

                        if (_member != null)
                        {
                            _result.message = $"동일한 facebook-id({app_user.facebook_id})가 이미 사용 중 입니다";
                            break;
                        }

                        if (__usermgr.AddNewMember(__db_context, app_user) == false)
                        {
                            _result.message = $"모바일 장치 정보 오류 입니다";
                            break;
                        }

                        _result.message = $"신규 회원으로 등록 되었습니다";
                        _result.success = true;
                    }
                    else
                    {
                        if (_member.IsAlive == true)
                        {
                            _result.message = $"다른 회원이 이미 사용하고 있는 메일 주소({app_user.mail_address}) 입니다";
                            break;
                        }

                        if (__usermgr.ReUseMember(__db_context, _member, app_user) == false)
                        {
                            _result.message = $"모바일 장치 정보 오류 입니다";
                            break;
                        }

                        _result.message = $"기존 회원으로 등록 되었습니다";
                        _result.success = true;
                    }

                    // 가입 member(회원)에게 즉시 메일 발송 하도록 큐에 명령을 보냅니다.
                    if (_result.success == true)
                    {
                        var _choice = new TChoice()
                        {
                            login_id = app_user.login_id,
                            sequence_no = WinnerReader.GetNextWeekSequenceNo(),
                            resend = true
                        };

                        await __memberQ.SendQAsync(_choice);
                    }

                    break;
                }

                return new OkObjectResult(new
                {
                    success = _result.success,
                    message = _result.message,

                    result = ""
                });
            });
        }

        [Route("UpdateUserInfor")]
        [Authorize(Policy = "LottoLionMember")]
        [HttpPost]
        public async Task<IActionResult> UpdateUserInfor(ApplicationUser app_user)
        {
            return await CProxy.Using(() =>
            {
                var _result = (success: false, message: "ok");

                while (true)
                {
                    var _login_id = __usermgr.GetLoginId(Request);
                    if (String.IsNullOrEmpty(_login_id) == true)
                    {
                        _result.message = "인증 정보에서 회원ID를 찾을 수 없습니다";
                        break;
                    }

                    var _member = __db_context.TbLionMember
                                        .Where(m => m.LoginId == _login_id && m.IsAlive == true)
                                        .SingleOrDefault();

                    if (_member == null)
                    {
                        _result.message = "회원님의 정보에 오류가 있습니다, 관리자에게 문의 하세요";
                        break;
                    }
                    else
                    {
                        if (String.IsNullOrEmpty(app_user.login_name) == false)
                            _member.LoginName = app_user.login_name;

                        if (app_user.max_select_number < 5)
                            _member.MaxSelectNumber = 5;
                        else if (app_user.max_select_number > 100)
                            _member.MaxSelectNumber = 100;
                        else
                            _member.MaxSelectNumber = app_user.max_select_number;

                        _member.Digit1 = (app_user.digit1 >= 1 && app_user.digit1 <= 45) ? app_user.digit1 : (short)0;
                        _member.Digit2 = (app_user.digit2 >= 1 && app_user.digit2 <= 45) ? app_user.digit2 : (short)0;
                        _member.Digit3 = (app_user.digit3 >= 1 && app_user.digit3 <= 45) ? app_user.digit3 : (short)0;

                        __db_context.SaveChanges();

                        _result.message = "회원 정보가 변경 되었습니다";
                        _result.success = true;
                    }

                    break;
                }

                return new OkObjectResult(new
                {
                    success = _result.success,
                    message = _result.message,

                    result = ""
                });
            });
        }

        [Route("UpdateMailAddress")]
        [Authorize(Policy = "LottoLionMember")]
        [HttpPost]
        public async Task<IActionResult> UpdateMailAddress(string mail_address, string check_number)
        {
            return await CProxy.Using(() =>
            {
                var _result = (success: false, message: "ok");

                while (true)
                {
                    var _login_id = __usermgr.GetLoginId(Request);
                    if (String.IsNullOrEmpty(_login_id) == true)
                    {
                        _result.message = "인증 정보에서 회원ID를 찾을 수 없습니다";
                        break;
                    }

                    var _value = "";

                    var _validate = __verify_email.TryRemove(mail_address, out _value);
                    if (_validate == false)
                    {
                        _result.message = $"검증 하고자 하는 메일 주소({mail_address})가 아닙니다";
                        break;
                    }

                    _validate = _value == check_number;
                    if (_validate == false)
                    {
                        _result.message = $"메일 검증 번호({check_number})가 일치 하지 않습니다";
                        break;
                    }

                    var _member = __db_context.TbLionMember
                                            .Where(m => m.LoginId == _login_id && m.IsAlive == true)
                                            .SingleOrDefault();

                    if (_member == null)
                    {
                        _result.message = "회원님의 정보에 오류가 있습니다, 관리자에게 문의 하세요";
                        break;
                    }

                    var _mail_exist = __db_context.TbLionMember
                                            .Where(m => m.EmailAddress == mail_address)
                                            .SingleOrDefault();

                    if (_mail_exist != null)
                    {
                        _result.message = "동일한 메일 주소를 이미 사용 중 입니다, 다른 메일 주소를 사용 하세요";
                        break;
                    }
                    else
                    {
                        _member.EmailAddress = mail_address;
                        __db_context.SaveChanges();

                        _result.message = $"메일 주소가 '{mail_address}'로 변경 되었습니다";
                        _result.success = true;
                    }

                    break;
                }

                return new OkObjectResult(new
                {
                    success = _result.success,
                    message = _result.message,

                    result = ""
                });
            });
        }

        [Route("UpdateDeviceId")]
        [Authorize(Policy = "LottoLionMember")]
        [HttpPost]
        public async Task<IActionResult> UpdateDeviceId(ApplicationUser app_user)
        {
            return await CProxy.Using(() =>
            {
                var _result = (success: false, message: "ok");

                while (true)
                {
                    var _login_id = __usermgr.GetLoginId(Request);
                    if (String.IsNullOrEmpty(_login_id) == true)
                    {
                        _result.message = "인증 정보에서 회원ID를 찾을 수 없습니다";
                        break;
                    }

                    var _member = __db_context.TbLionMember
                                            .Where(m => m.LoginId == _login_id && m.IsAlive == true)
                                            .SingleOrDefault();

                    if (_member == null)
                    {
                        _result.message = "회원님의 정보에 오류가 있습니다, 관리자에게 문의 하세요";
                        break;
                    }

                    if (_member.DeviceType == app_user.device_type && _member.DeviceId == app_user.device_id)
                    {
                        _result.message = "동일한 장치 정보 입니다";
                        break;
                    }
                    else
                    {
                        _member.DeviceType = app_user.device_type;
                        _member.DeviceId = app_user.device_id;
                        __db_context.SaveChanges();

                        _result.message = "장치 정보가 변경 되었습니다";
                        _result.success = true;
                    }

                    break;
                }

                return new OkObjectResult(new
                {
                    success = _result.success,
                    message = _result.message,

                    result = ""
                });
            });
        }

        [Route("ChangePassword")]
        [Authorize(Policy = "LottoLionMember")]
        [HttpPost]
        public async Task<IActionResult> ChangePassword(ApplicationUser app_user)
        {
            return await CProxy.Using(() =>
            {
                var _result = (success: false, message: "ok");

                while (true)
                {
                    var _login_id = __usermgr.GetLoginId(Request);
                    if (String.IsNullOrEmpty(_login_id) == true)
                    {
                        _result.message = "인증 정보에서 회원ID를 찾을 수 없습니다";
                        break;
                    }

                    var _member = __db_context.TbLionMember
                                            .Where(m => m.LoginId == _login_id && m.IsAlive == true)
                                            .SingleOrDefault();

                    if (_member == null)
                    {
                        _result.message = "회원님의 정보에 오류가 있습니다, 관리자에게 문의 하세요";
                        break;
                    }

                    if (String.IsNullOrEmpty(app_user.password) == true)
                    {
                        _result.message = "암호를 입력 하세요";
                        break;
                    }
                    else
                    {
                        _member.LoginPassword = __usermgr.ComupteHashString(app_user.password);
                        __db_context.SaveChanges();

                        _result.message = "암호가 변경 되었습니다";
                        _result.success = true;
                    }

                    break;
                }

                return new OkObjectResult(new
                {
                    success = _result.success,
                    message = _result.message,

                    result = ""
                });
            });
        }

        [Route("LeaveMember")]
        [Authorize(Policy = "LottoLionMember")]
        [HttpPost]
        public async Task<IActionResult> LeaveMember()
        {
            return await CProxy.Using(() =>
            {
                var _result = (success: false, message: "ok");

                while (true)
                {
                    var _login_id = __usermgr.GetLoginId(Request);
                    if (String.IsNullOrEmpty(_login_id) == true)
                    {
                        _result.message = "인증 정보에서 회원ID를 찾을 수 없습니다";
                        break;
                    }

                    var _member = __db_context.TbLionMember
                                            .Where(m => m.LoginId == _login_id && m.IsAlive == true)
                                            .SingleOrDefault();

                    if (_member == null)
                    {
                        _result.message = "회원님의 정보에 오류가 있습니다, 관리자에게 문의 하세요";
                        break;
                    }
                    else
                    {
                        _member.IsAlive = false;
                        __db_context.SaveChanges();

                        _result.message = "탈퇴 처리가 완료 되었습니다";
                        _result.success = true;
                    }

                    break;
                }

                return new OkObjectResult(new
                {
                    success = _result.success,
                    message = _result.message,

                    result = ""
                });
            });
        }
    }
}