﻿using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using FileSharing.Models;
using FileSharing.Services;
using Microsoft.AspNetCore.Identity.UI.Services;
using FileSharing.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.Cookies;
using NuGet.Versioning;
using Microsoft.IdentityModel.Tokens;
using NuGet.Protocol;
using System.Security.Claims;

namespace FileSharing.Controllers
{
    public class AccountController : Controller
    {

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly IRenderService _renderService;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IEmailSender emailSender,
            IRenderService renderService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _renderService = renderService;
        }

        /* GET */
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpGet]
        public IActionResult SignIn()
        {
            return View();
        }

        [HttpGet]
        public IActionResult RequestPasswordReset()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ResetPassword(string token, string email)
        {
            if (token == null || email == null)
            {
                return BadRequest();
            }

            var model = new ResetPasswordViewModel { Token = token, Email = email };
            return View(model);
        }

        [HttpGet]
        public IActionResult ResetPasswordConfirmation()
        {
            return View();
        }

        [HttpGet]
        public  async Task GoogleLogin()
        {
            await HttpContext.ChallengeAsync(GoogleDefaults.AuthenticationScheme,
                new AuthenticationProperties
                {
                    RedirectUri=Url.Action("GoogleResponse")
                });
        }

        [HttpGet]
        public async Task<IActionResult> GoogleResponse()
        {
            // Authenticate using the Google scheme
            var authenticateResult = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);

            if (!authenticateResult.Succeeded)
            {
                return BadRequest();
            }

            // Extract the user's email from the claims
            var emailClaim = authenticateResult.Principal.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(emailClaim))
            {
                return BadRequest("Email claim not found.");
            }

            // Check if the user already exists in the local database
            var user = await _userManager.FindByEmailAsync(emailClaim);
            if (user == null)
            {
                // Create a new local user account with the Google email
                user = new ApplicationUser
                {
                    UserName = emailClaim,
                    Email = emailClaim
                };
                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    // Handle account creation failure
                    return BadRequest(createResult.Errors);
                }
            }

            // Sign in the user
            await _signInManager.SignInAsync(user, isPersistent: true);

            // Redirect to the Upload action
            return RedirectToAction("Upload", "Upload");
        }

        /* POST */
        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid) 
            {
                var user = new ApplicationUser { UserName = model.Email, Email = model.Email };
                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    bool CanSignIn = await _signInManager.CanSignInAsync(user);
                    if(CanSignIn)
                    {
                        SignInViewModel signInViewModel = new SignInViewModel()
                        {
                            Email = model.Email,
                            Password = model.Password
                        };
                        await SignIn(signInViewModel);
                        return RedirectToAction("Upload", "Upload");
                    }
                }

                foreach(var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> SignIn(SignInViewModel model)
        {
            if(ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if(user != null)
                {
                    var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, true, false);

                    if (result.Succeeded)
                    {
                        return RedirectToAction("Upload", "Upload");
                    }
                    else if (result.IsLockedOut) 
                    {
                        // lockout is turned off, but its ready to accept the error if enabled.
                        ModelState.AddModelError(string.Empty, "This account is locked, please try again later.");
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        public async Task<IActionResult> RequestPasswordReset(PasswordResetViewModel model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "User not found.");
                return View(model);
            }

            string resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetUrl = Url.Action("ResetPassword", "Account", new { token = resetToken, email = model.Email }, Request.Scheme);

            if(resetUrl == null)
            {
                return BadRequest("Reset URL not valid");
            }
            PasswordResetEmailViewModel resetModel = new PasswordResetEmailViewModel();
            resetModel.UserName = model.Email;
            resetModel.ResetUrl = resetUrl;
            string emailHtml = await _renderService.RenderToStringAsync("Account/PasswordResetEmail", resetModel);
            await _emailSender.SendEmailAsync(user.Email ?? "noemail@invalid.com", "Password Reset From FileSharing", emailHtml);

            return View("PasswordResetRequestConfirmation");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                // Don't reveal that the user does not exist
                return RedirectToAction("ResetPasswordConfirmation");
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
            if (result.Succeeded)
            {
                return RedirectToAction("ResetPasswordConfirmation");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

    }
}
