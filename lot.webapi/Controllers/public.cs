﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using OdinSdk.BaseLib.WebApi;

namespace LottoLion.WebApi.Controllers
{
    public partial class UserController
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [EnableCors("CorsPolicy")]
        [Route("GetWinners2")]
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetWinners2()
        {
            return await CProxy.Using(() =>
            {
                var _result = __db_context.TbLionWinner
                                    .OrderByDescending(w => w.SequenceNo)
                                    .ToList();

                return new OkObjectResult(new
                {
                    success = true,
                    message = "",

                    result = _result
                });
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [Route("GetWinners")]
        [Authorize(Policy = "LottoLionUsers")]
        [HttpPost]
        public async Task<IActionResult> GetWinners()
        {
            return await CProxy.Using(() =>
            {
                var _result = __db_context.TbLionWinner
                                    .OrderByDescending(w => w.SequenceNo)
                                    .ToList();

                return new OkObjectResult(new
                {
                    success = true,
                    message = "",

                    result = _result
                });
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [Route("GetAnalysis")]
        [Authorize(Policy = "LottoLionUsers")]
        [HttpPost]
        public async Task<IActionResult> GetAnalysis()
        {
            return await CProxy.Using(() =>
            {
                var _result = __db_context.TbLionAnalysis
                                    .OrderByDescending(w => w.SequenceNo)
                                    .ToList();

                return new OkObjectResult(new
                {
                    success = true,
                    message = "",

                    result = _result
                });
            });
        }
    }
}