using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payaffe.Api.Tests.Payments;
using Payaffe.Application.Admin;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;

namespace Payaffe.Api.Tests.Admin;

public sealed class AdminAuthApiTests
{
    private static readonly byte[] TotpSecret = "12345678901234567890"u8.ToArray();

    [Fact]
    public async Task Login_start_returns_mfa_required_for_valid_password_without_exposing_admin_account()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = " admin@example.test ",
                password = "correct-password",
            });

        response.EnsureSuccessStatusCode();
        var loginStart = await response.Content.ReadFromJsonAsync<LoginStartResponse>();
        Assert.Equal("mfa_required", loginStart!.Status);
        Assert.NotEqual(Guid.Empty, loginStart.ChallengeId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(adminAccountId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var challenge = Assert.Single(dbContext.AdminLoginChallenges);
        Assert.Equal(adminAccountId, challenge.AdminAccountId);
        Assert.Null(challenge.ConsumedAt);
        var auditEntry = Assert.Single(dbContext.AuditLogEntries);
        Assert.Equal("admin.login_start", auditEntry.EventType);
        Assert.Equal("success", auditEntry.Outcome);
        Assert.Equal("product_user", auditEntry.ActorType);
        Assert.Equal(adminAccountId.ToString("D"), auditEntry.ActorId);
        Assert.Equal("admin_login.password_verified", auditEntry.ReasonCode);
    }

    /// <summary>
    /// The sign-in an installation has right after `bootstrap-admin`: no second
    /// factor enrolled, so the password is the whole thing (ADR 0028).
    /// </summary>
    [Fact]
    public async Task Login_start_authenticates_an_account_without_a_second_factor()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecretReference: null);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "admin@example.test",
                password = "correct-password",
            });

        response.EnsureSuccessStatusCode();
        var loginStart = await response.Content.ReadFromJsonAsync<LoginStartResponse>();
        Assert.Equal("authenticated", loginStart!.Status);
        Assert.Null(loginStart.ChallengeId);

        // The session cookie is the point: the caller is signed in, with no
        // second step to come.
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith("__Host-payaffe-admin=", StringComparison.Ordinal));

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(adminAccountId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();

        // No challenge row: there was no second step to remember.
        Assert.Empty(dbContext.AdminLoginChallenges);
        var session = Assert.Single(dbContext.AdminSessions);
        Assert.Equal(adminAccountId, session.AdminAccountId);

        // Honest about what it cleared: a password sign-in did not pass MFA,
        // and the security store says so rather than recording a time.
        Assert.Null(session.MfaAuthenticatedAt);
        Assert.Null(session.StepUpAuthenticatedAt);

        var auditEntry = Assert.Single(dbContext.AuditLogEntries);
        Assert.Equal("admin.login_complete", auditEntry.EventType);
        Assert.Equal("success", auditEntry.Outcome);
        Assert.Equal("admin_login.password_only", auditEntry.ReasonCode);
        Assert.Equal("admin_session", auditEntry.SubjectType);
    }

    [Fact]
    public async Task Login_start_returns_generic_error_and_audits_failure_for_invalid_password()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync("admin@example.test", "correct-password");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "admin@example.test",
                password = "wrong-password",
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_login.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-password", body, StringComparison.Ordinal);
        Assert.DoesNotContain(adminAccountId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var adminAccount = Assert.Single(dbContext.AdminAccounts);
        Assert.Equal(1, adminAccount.FailedPasswordAttemptCount);
        Assert.Null(adminAccount.LockedUntil);
        var auditEntry = Assert.Single(dbContext.AuditLogEntries);
        Assert.Equal("failure", auditEntry.Outcome);
        Assert.Equal("admin_login.invalid_credentials", auditEntry.ReasonCode);
        Assert.Equal(adminAccountId.ToString("D"), auditEntry.SubjectId);
    }

    [Fact]
    public async Task Login_start_rate_limit_returns_generic_error_and_audits_denial()
    {
        await using var factory = new PaymentApiFactory
        {
            AdminRateLimitPermitLimit = 1,
            AdminRateLimitWindow = TimeSpan.FromMinutes(5),
        };
        await factory.SeedAdminAccountAsync("admin@example.test", "correct-password");
        using var client = factory.CreateClient();
        var firstResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "admin@example.test",
                password = "wrong-password",
            });
        Assert.Equal(HttpStatusCode.Unauthorized, firstResponse.StatusCode);

        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "admin@example.test",
                password = "wrong-password",
            });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_rate_limit.exceeded", body, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-password", body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.rate_limit" &&
            entry.Outcome == "denied" &&
            entry.ActorType == "system" &&
            entry.ActorId == "unknown" &&
            entry.SubjectType == "admin_account" &&
            entry.SubjectId == "unknown" &&
            entry.ReasonCode == "admin_rate_limit.exceeded");
    }

    [Fact]
    public async Task Mfa_complete_sets_secure_session_cookie_and_stores_only_session_token_hash()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "admin@example.test",
                password = "correct-password",
            });
        var loginStart = await loginResponse.Content.ReadFromJsonAsync<LoginStartResponse>();

        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/mfa",
            new
            {
                challengeId = loginStart!.ChallengeId,
                totpCode = ComputeTotpCode(TotpSecret, DateTimeOffset.UtcNow),
            });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("""{"status":"authenticated"}""", body);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
        var sessionCookie = Assert.Single(cookieHeaders, cookie => cookie.StartsWith("__Host-payaffe-admin=", StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        var rawSessionToken = ExtractCookieValue(sessionCookie);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var session = Assert.Single(dbContext.AdminSessions);
        Assert.Equal(adminAccountId, session.AdminAccountId);
        Assert.StartsWith("sha256:", session.TokenHash, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, session.TokenHash, StringComparison.Ordinal);
        Assert.Single(dbContext.AdminLoginChallenges, challenge => challenge.ConsumedAt != null);
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.mfa_complete" &&
            entry.Outcome == "success" &&
            entry.SubjectId == session.Id.ToString("D"));
    }

    [Fact]
    public async Task Mfa_complete_accepts_active_recovery_code_once_and_audits_use()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var recoveryCodeId = await SeedRecoveryCodeAsync(factory, adminAccountId, "ABCD-EFGH-JK23");
        using var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "admin@example.test",
                password = "correct-password",
            });
        var loginStart = await loginResponse.Content.ReadFromJsonAsync<LoginStartResponse>();

        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/mfa",
            new
            {
                challengeId = loginStart!.ChallengeId,
                recoveryCode = " abcd-efgh-jk23 ",
            });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("""{"status":"authenticated"}""", body);
        Assert.DoesNotContain("ABCD-EFGH-JK23", body, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
        var sessionCookie = Assert.Single(cookieHeaders, cookie => cookie.StartsWith("__Host-payaffe-admin=", StringComparison.Ordinal));
        var rawSessionToken = ExtractCookieValue(sessionCookie);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var session = Assert.Single(dbContext.AdminSessions);
        Assert.Equal(adminAccountId, session.AdminAccountId);
        Assert.DoesNotContain(rawSessionToken, session.TokenHash, StringComparison.Ordinal);
        var recoveryCode = Assert.Single(dbContext.AdminRecoveryCodes);
        Assert.Equal(recoveryCodeId, recoveryCode.Id);
        Assert.Equal("used", recoveryCode.Status);
        Assert.NotNull(recoveryCode.UsedAt);
        Assert.DoesNotContain("ABCD-EFGH-JK23", recoveryCode.CodeHash, StringComparison.OrdinalIgnoreCase);
        Assert.Single(dbContext.AdminLoginChallenges, challenge => challenge.ConsumedAt != null);
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.mfa_complete" &&
            entry.Outcome == "success" &&
            entry.ActorId == adminAccountId.ToString("D") &&
            entry.SubjectId == session.Id.ToString("D") &&
            entry.ReasonCode == "admin_mfa.recovery_code_verified");
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.recovery_codes.use" &&
            entry.Outcome == "success" &&
            entry.ActorId == adminAccountId.ToString("D") &&
            entry.SubjectType == "admin_account" &&
            entry.SubjectId == adminAccountId.ToString("D") &&
            entry.ReasonCode == "admin_recovery_codes.used");
        Assert.DoesNotContain(dbContext.AuditLogEntries, entry =>
            entry.ActorId.Contains("ABCD-EFGH-JK23", StringComparison.OrdinalIgnoreCase) ||
            entry.SubjectId.Contains("ABCD-EFGH-JK23", StringComparison.OrdinalIgnoreCase) ||
            entry.ReasonCode.Contains("ABCD-EFGH-JK23", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Mfa_complete_rejects_used_recovery_code_with_generic_response()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        await SeedRecoveryCodeAsync(factory, adminAccountId, "ABCD-EFGH-JK23");
        using var client = factory.CreateClient();
        var firstLoginResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "admin@example.test",
                password = "correct-password",
            });
        var firstLoginStart = await firstLoginResponse.Content.ReadFromJsonAsync<LoginStartResponse>();
        var firstMfaResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/mfa",
            new
            {
                challengeId = firstLoginStart!.ChallengeId,
                recoveryCode = "ABCD-EFGH-JK23",
            });
        firstMfaResponse.EnsureSuccessStatusCode();

        var secondLoginResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "admin@example.test",
                password = "correct-password",
            });
        var secondLoginStart = await secondLoginResponse.Content.ReadFromJsonAsync<LoginStartResponse>();
        var secondMfaResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/mfa",
            new
            {
                challengeId = secondLoginStart!.ChallengeId,
                recoveryCode = "ABCD-EFGH-JK23",
            });

        Assert.Equal(HttpStatusCode.Unauthorized, secondMfaResponse.StatusCode);
        var body = await secondMfaResponse.Content.ReadAsStringAsync();
        Assert.Contains("admin_mfa.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCD-EFGH-JK23", body, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Single(dbContext.AdminSessions);
        var recoveryCode = Assert.Single(dbContext.AdminRecoveryCodes);
        Assert.Equal("used", recoveryCode.Status);
        Assert.NotNull(recoveryCode.UsedAt);
        var failedChallenge = Assert.Single(
            dbContext.AdminLoginChallenges,
            challenge => challenge.Id == secondLoginStart!.ChallengeId);
        Assert.Equal(1, failedChallenge.FailedAttemptCount);
        Assert.Null(failedChallenge.ConsumedAt);
        Assert.Equal(1, dbContext.AuditLogEntries.Count(entry => entry.EventType == "admin.recovery_codes.use"));
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.mfa_complete" &&
            entry.Outcome == "failure" &&
            entry.ReasonCode == "admin_mfa.invalid");
    }

    [Fact]
    public async Task Get_admin_session_authenticates_session_cookie_and_refreshes_idle_expiration()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.GetAsync("/api/admin/session");

        response.EnsureSuccessStatusCode();
        var sessionResponse = await response.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal("authenticated", sessionResponse!.Status);
        Assert.Equal(adminAccountId, sessionResponse.AdminAccountId);
        Assert.Equal("admin@example.test", sessionResponse.Username);
        Assert.True(sessionResponse.StepUpAuthenticatedAt >= sessionResponse.MfaAuthenticatedAt);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var session = Assert.Single(dbContext.AdminSessions);
        Assert.True(session.LastSeenAt > session.CreatedAt);
        Assert.Equal(session.IdleExpiresAt, sessionResponse.IdleExpiresAt);
    }

    [Fact]
    public async Task Get_admin_session_returns_generic_error_without_session_cookie()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_session.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("__Host-payaffe-admin", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_admin_session_rejects_expired_session_cookie()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var session = Assert.Single(dbContext.AdminSessions);
            session.IdleExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await dbContext.SaveChangesAsync();
        }

        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");
        var response = await sessionClient.GetAsync("/api/admin/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_session.invalid", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_admin_payments_requires_valid_admin_session()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/payments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_session.invalid", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_admin_payments_returns_recent_payment_summaries_for_authenticated_admin()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var credentialId = await factory.SeedCredentialAsync("integration-token");
        var now = DateTimeOffset.UtcNow;
        await SeedPaymentAsync(
            factory,
            credentialId,
            "older-order",
            "pending_currency_selection",
            now.AddMinutes(-10));
        var newestPaymentId = await SeedPaymentAsync(
            factory,
            credentialId,
            "newer-order",
            "waiting_for_payment",
            now,
            selectedCurrency: "BTC",
            expectedCryptoAmount: "0.00039980",
            paymentAddress: "btc-test-address");
        using var signInClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(signInClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.GetAsync(
            $"/api/admin/payments?projectId={ProjectDefaults.DefaultProjectId:D}&limit=1");

        response.EnsureSuccessStatusCode();
        var adminPayments = await response.Content.ReadFromJsonAsync<AdminPaymentsResponse>();
        var payment = Assert.Single(adminPayments!.Payments);
        Assert.Equal(newestPaymentId, payment.PaymentId);
        Assert.Equal("newer-order", payment.ExternalReference);
        Assert.Equal("waiting_for_payment", payment.Status);
        Assert.Equal("EUR", payment.FiatCurrency);
        Assert.Equal(1999, payment.FiatAmountMinor);
        Assert.Equal("BTC", payment.SelectedCurrency);
        Assert.Equal("0.00039980", payment.ExpectedCryptoAmount);
        Assert.Equal("btc-test-address", payment.PaymentAddress);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        var missingProjectResponse = await sessionClient.GetAsync("/api/admin/payments");
        Assert.Equal(HttpStatusCode.BadRequest, missingProjectResponse.StatusCode);
        Assert.Contains(
            "project_id.required",
            await missingProjectResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_payment_reads_reject_a_payment_from_another_Project()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var credentialId = await factory.SeedCredentialAsync("integration-token");
        var paymentId = await SeedPaymentAsync(
            factory,
            credentialId,
            "project-isolated-order",
            "waiting_for_payment",
            DateTimeOffset.UtcNow);
        var otherProjectId = await factory.SeedProjectAsync();
        using var signInClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(signInClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var listResponse = await sessionClient.GetAsync(
            $"/api/admin/payments?projectId={otherProjectId:D}");
        listResponse.EnsureSuccessStatusCode();
        var listed = await listResponse.Content.ReadFromJsonAsync<AdminPaymentsResponse>();
        Assert.Empty(listed!.Payments);

        var detailResponse = await sessionClient.GetAsync(
            $"/api/admin/payments/{paymentId:D}?projectId={otherProjectId:D}");
        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);

        var settleResponse = await sessionClient.PostAsJsonAsync(
            $"/api/admin/payments/{paymentId:D}/settle",
            new { projectId = otherProjectId, expectedVersion = 1, reason = "Wrong Project" });
        Assert.Equal(HttpStatusCode.NotFound, settleResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.payment.settle" &&
            entry.Outcome == "denied" &&
            entry.ProjectId == otherProjectId &&
            entry.SubjectId == paymentId.ToString("D"));
    }

    [Fact]
    public async Task Admin_can_create_list_and_transition_a_Project_with_audit_attribution()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var loginClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(loginClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var createResponse = await sessionClient.PostAsJsonAsync(
            "/api/admin/projects",
            new { name = "Second Shop", slug = "second-shop" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<AdminProjectResponse>();
        Assert.NotNull(created);
        Assert.Equal("active", created.Project.Status);
        Assert.Equal(1, created.Project.Version);

        var listResponse = await sessionClient.GetAsync("/api/admin/projects");
        listResponse.EnsureSuccessStatusCode();
        var projects = await listResponse.Content.ReadFromJsonAsync<AdminProjectsResponse>();
        Assert.Contains(projects!.Projects, project => project.ProjectId == created.Project.ProjectId);

        var disableResponse = await sessionClient.PostAsJsonAsync(
            $"/api/admin/projects/{created.Project.ProjectId:D}/status",
            new { expectedVersion = 1, status = "disabled" });
        disableResponse.EnsureSuccessStatusCode();
        var disabled = await disableResponse.Content.ReadFromJsonAsync<AdminProjectResponse>();
        Assert.Equal("disabled", disabled!.Project.Status);
        Assert.Equal(2, disabled.Project.Version);

        var archiveResponse = await sessionClient.PostAsJsonAsync(
            $"/api/admin/projects/{created.Project.ProjectId:D}/status",
            new { expectedVersion = 2, status = "archived" });
        archiveResponse.EnsureSuccessStatusCode();
        var archived = await archiveResponse.Content.ReadFromJsonAsync<AdminProjectResponse>();
        Assert.Equal("archived", archived!.Project.Status);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var auditEntries = dbContext.AuditLogEntries
            .Where(entry => entry.ProjectId == created.Project.ProjectId)
            .ToArray();
        Assert.Equal(3, auditEntries.Length);
        Assert.All(auditEntries, entry => Assert.Equal(adminAccountId.ToString("D"), entry.ActorId));
    }

    [Fact]
    public async Task Get_admin_payment_detail_requires_valid_admin_session()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/admin/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_session.invalid", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_admin_payment_detail_returns_not_found_for_unknown_payment()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var signInClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(signInClient);
        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={ExtractCookieValue(sessionCookie)}");

        var response = await sessionClient.GetAsync(
            $"/api/admin/payments/{Guid.NewGuid()}?projectId={ProjectDefaults.DefaultProjectId:D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("payment.not_found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_admin_payment_detail_returns_operational_payment_fields()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var credentialId = await factory.SeedCredentialAsync("integration-token");
        var createdAt = DateTimeOffset.UtcNow;
        var paymentId = await SeedPaymentAsync(
            factory,
            credentialId,
            "observed-order",
            "observed",
            createdAt,
            selectedCurrency: "BTC",
            expectedCryptoAmount: "0.00039980",
            paymentAddress: "btc-test-address");
        await SeedObservationAsync(factory, paymentId, "0.00020000", createdAt.AddMinutes(3));
        await SeedObservationAsync(factory, paymentId, "0.00019980", createdAt.AddMinutes(5));
        using var signInClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(signInClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.GetAsync(
            $"/api/admin/payments/{paymentId}?projectId={ProjectDefaults.DefaultProjectId:D}");

        response.EnsureSuccessStatusCode();
        var payment = await response.Content.ReadFromJsonAsync<AdminPaymentDetailResponse>();
        Assert.Equal(paymentId, payment!.PaymentId);
        Assert.Equal("observed-order", payment.ExternalReference);
        Assert.Equal("observed", payment.Status);
        Assert.Equal("BTC", payment.SelectedCurrency);
        Assert.Equal("0.00039980", payment.ExpectedCryptoAmount);
        Assert.Equal("btc-test-address", payment.PaymentAddress);
        Assert.Equal("0.0003998", payment.ObservedTotal);
        Assert.Null(payment.ConfirmedEligibleTotal);
        Assert.NotEqual(DateTimeOffset.MinValue, payment.LateAcceptanceEndsAt);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_admin_audit_log_requires_valid_admin_session()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/audit-log");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_session.invalid", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_admin_audit_log_returns_recent_entries_and_audits_access()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var signInClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(signInClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var occurredAt = DateTimeOffset.UtcNow;
        await SeedAuditEntryAsync(
            factory,
            occurredAt,
            "admin.login_start",
            "success",
            adminAccountId,
            "admin_login.password_verified");
        var newestEventId = await SeedAuditEntryAsync(
            factory,
            occurredAt.AddMinutes(1),
            "admin.logout",
            "success",
            adminAccountId,
            "admin_logout.session_revoked",
            sourceIp: "203.0.113.10",
            userAgent: "SensitiveUserAgent/1.0");
        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.GetAsync("/api/admin/audit-log?limit=1");

        response.EnsureSuccessStatusCode();
        var auditLog = await response.Content.ReadFromJsonAsync<AdminAuditLogResponse>();
        var entry = Assert.Single(auditLog!.Entries);
        Assert.Equal(newestEventId, entry.EventId);
        Assert.Equal("admin.logout", entry.EventType);
        Assert.Equal("success", entry.Outcome);
        Assert.Equal("product_user", entry.ActorType);
        Assert.Equal(adminAccountId.ToString("D"), entry.ActorId);
        Assert.Equal("admin_logout.session_revoked", entry.ReasonCode);
        Assert.Equal("admin_session", entry.SubjectType);
        Assert.Equal("recent-session", entry.SubjectId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.10", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SensitiveUserAgent/1.0", body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(dbContext.AuditLogEntries, auditEntry =>
            auditEntry.EventType == "admin.audit_log.list" &&
            auditEntry.Outcome == "success" &&
            auditEntry.ActorId == adminAccountId.ToString("D") &&
            auditEntry.SubjectType == "audit_log" &&
            auditEntry.SubjectId == "recent" &&
            auditEntry.ReasonCode == "audit_log.listed");
    }

    [Fact]
    public async Task Get_admin_audit_log_detail_requires_valid_admin_session()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/admin/audit-log/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_session.invalid", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_admin_audit_log_detail_requires_recent_step_up_and_audits_denial()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var signInClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(signInClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var eventId = await SeedAuditEntryAsync(
            factory,
            DateTimeOffset.UtcNow,
            "admin.logout",
            "success",
            adminAccountId,
            "admin_logout.session_revoked");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var session = Assert.Single(dbContext.AdminSessions);
            session.StepUpAuthenticatedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
            await dbContext.SaveChangesAsync();
        }

        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.GetAsync($"/api/admin/audit-log/{eventId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_step_up.required", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(verifyDbContext.AuditLogEntries, auditEntry =>
            auditEntry.EventType == "admin.audit_log.detail" &&
            auditEntry.Outcome == "denied" &&
            auditEntry.ActorId == adminAccountId.ToString("D") &&
            auditEntry.SubjectType == "audit_log" &&
            auditEntry.SubjectId == eventId.ToString("D") &&
            auditEntry.ReasonCode == "admin_step_up.required");
    }

    /// <summary>
    /// A session begun on the password alone keeps no exemption once the
    /// account enrolls a factor: the exemption follows the account, not the
    /// state it was in at sign-in.
    /// </summary>
    [Fact]
    public async Task Password_only_session_requires_step_up_once_the_account_enrolls_a_factor()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecretReference: null);
        using var signInClient = factory.CreateClient();
        var loginResponse = await signInClient.PostAsJsonAsync(
            "/api/admin/auth/login",
            new { username = "admin@example.test", password = "correct-password" });
        loginResponse.EnsureSuccessStatusCode();
        var passwordOnlyToken = ExtractCookieValue(Assert.Single(
            loginResponse.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith("__Host-payaffe-admin=", StringComparison.Ordinal)));
        var eventId = await SeedAuditEntryAsync(
            factory,
            DateTimeOffset.UtcNow,
            "admin.logout",
            "success",
            adminAccountId,
            "admin_logout.session_revoked");
        using var passwordOnlyClient = factory.CreateClient();
        passwordOnlyClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={passwordOnlyToken}");

        // Before enrollment there is nothing to step up with.
        var beforeEnrollment = await passwordOnlyClient.GetAsync($"/api/admin/audit-log/{eventId}");
        Assert.Equal(HttpStatusCode.OK, beforeEnrollment.StatusCode);

        await factory.EnrollTotpAsync(adminAccountId, "secret-ref:test-admin", TotpSecret);

        var afterEnrollment = await passwordOnlyClient.GetAsync($"/api/admin/audit-log/{eventId}");
        Assert.Equal(HttpStatusCode.Forbidden, afterEnrollment.StatusCode);
        Assert.Contains(
            "admin_step_up.required",
            await afterEnrollment.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // The first sign-in that proves the factor ends the session left over
        // from before it, and says so in the Audit Log.
        using var mfaClient = factory.CreateClient();
        await SignInAndGetSessionCookieAsync(mfaClient);

        var revoked = await passwordOnlyClient.GetAsync("/api/admin/session");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Single(dbContext.AdminSessions, session => session.RevokedAt == null);
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.sessions.revoke" &&
            entry.Outcome == "revoked" &&
            entry.ActorId == adminAccountId.ToString("D") &&
            entry.SubjectType == "admin_account" &&
            entry.ReasonCode == "admin_session.second_factor_enrolled");
    }

    [Fact]
    public async Task Get_admin_audit_log_detail_returns_detail_fields_and_audits_access()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var signInClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(signInClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var eventId = await SeedAuditEntryAsync(
            factory,
            DateTimeOffset.UtcNow,
            "admin.logout",
            "success",
            adminAccountId,
            "admin_logout.session_revoked",
            sourceIp: "203.0.113.10",
            userAgent: "SensitiveUserAgent/1.0");
        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.GetAsync($"/api/admin/audit-log/{eventId}");

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<AdminAuditLogEntryDetailResponse>();
        Assert.Equal(eventId, detail!.EventId);
        Assert.Equal("admin.logout", detail.EventType);
        Assert.Equal("success", detail.Outcome);
        Assert.Equal("product_user", detail.ActorType);
        Assert.Equal(adminAccountId.ToString("D"), detail.ActorId);
        Assert.Equal("api", detail.SourceService);
        Assert.Equal("203.0.113.10", detail.SourceIp);
        Assert.Equal("SensitiveUserAgent/1.0", detail.UserAgent);
        Assert.StartsWith("test-", detail.CorrelationId, StringComparison.Ordinal);
        Assert.Equal("admin_logout.session_revoked", detail.ReasonCode);
        Assert.Equal("admin_session", detail.SubjectType);
        Assert.Equal("recent-session", detail.SubjectId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(dbContext.AuditLogEntries, auditEntry =>
            auditEntry.EventType == "admin.audit_log.detail" &&
            auditEntry.Outcome == "success" &&
            auditEntry.ActorId == adminAccountId.ToString("D") &&
            auditEntry.SubjectType == "audit_log" &&
            auditEntry.SubjectId == eventId.ToString("D") &&
            auditEntry.ReasonCode == "audit_log.detail_accessed");
    }

    [Fact]
    public async Task Export_admin_audit_log_with_session_cookie_requires_csrf_evidence()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.PostAsJsonAsync("/api/admin/audit-log/export", new { limit = 10 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_csrf.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.DoesNotContain(dbContext.AuditLogEntries, auditEntry => auditEntry.EventType == "admin.audit_log.export");
    }

    [Fact]
    public async Task Export_admin_audit_log_requires_recent_step_up_and_audits_denial()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var session = Assert.Single(dbContext.AdminSessions);
            session.StepUpAuthenticatedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
            await dbContext.SaveChangesAsync();
        }

        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var response = await sessionClient.PostAsJsonAsync("/api/admin/audit-log/export", new { limit = 10 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_step_up.required", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(verifyDbContext.AuditLogEntries, auditEntry =>
            auditEntry.EventType == "admin.audit_log.export" &&
            auditEntry.Outcome == "denied" &&
            auditEntry.ActorId == adminAccountId.ToString("D") &&
            auditEntry.SubjectType == "audit_log" &&
            auditEntry.SubjectId == "recent" &&
            auditEntry.ReasonCode == "admin_step_up.required");
    }

    [Fact]
    public async Task Export_admin_audit_log_returns_recent_detail_entries_and_audits_success()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var olderEventId = await SeedAuditEntryAsync(
            factory,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "admin.login_start",
            "success",
            adminAccountId,
            "admin_login.password_verified");
        var newestEventId = await SeedAuditEntryAsync(
            factory,
            DateTimeOffset.UtcNow,
            "admin.logout",
            "success",
            adminAccountId,
            "admin_logout.session_revoked",
            sourceIp: "203.0.113.10",
            userAgent: "SensitiveUserAgent/1.0");
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var response = await sessionClient.PostAsJsonAsync("/api/admin/audit-log/export", new { limit = 1 });

        response.EnsureSuccessStatusCode();
        var export = await response.Content.ReadFromJsonAsync<AdminAuditLogExportResponse>();
        Assert.NotEqual(DateTimeOffset.MinValue, export!.ExportedAt);
        var entry = Assert.Single(export.Entries);
        Assert.Equal(newestEventId, entry.EventId);
        Assert.NotEqual(olderEventId, entry.EventId);
        Assert.Equal("203.0.113.10", entry.SourceIp);
        Assert.Equal("SensitiveUserAgent/1.0", entry.UserAgent);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(dbContext.AuditLogEntries, auditEntry =>
            auditEntry.EventType == "admin.audit_log.export" &&
            auditEntry.Outcome == "success" &&
            auditEntry.ActorId == adminAccountId.ToString("D") &&
            auditEntry.SubjectType == "audit_log" &&
            auditEntry.SubjectId == "recent" &&
            auditEntry.ReasonCode == "audit_log.exported");
    }

    [Fact]
    public async Task Audit_export_filters_by_Project_and_global_export_is_explicit()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var firstProjectId = await factory.SeedProjectAsync();
        var secondProjectId = await factory.SeedProjectAsync();
        var firstEventId = await SeedAuditEntryAsync(
            factory,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "admin.payment.settle",
            "success",
            adminAccountId,
            "payment.settled",
            projectId: firstProjectId);
        var secondEventId = await SeedAuditEntryAsync(
            factory,
            DateTimeOffset.UtcNow,
            "admin.payment.settle",
            "denied",
            adminAccountId,
            "payment.not_found",
            projectId: secondProjectId);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var filteredResponse = await sessionClient.PostAsJsonAsync(
            "/api/admin/audit-log/export",
            new { projectId = firstProjectId, limit = 100 });
        filteredResponse.EnsureSuccessStatusCode();
        var filtered = await filteredResponse.Content.ReadFromJsonAsync<AdminAuditLogExportResponse>();
        Assert.Contains(filtered!.Entries, entry => entry.EventId == firstEventId);
        Assert.DoesNotContain(filtered.Entries, entry => entry.EventId == secondEventId);
        Assert.All(filtered.Entries, entry => Assert.Equal(firstProjectId, entry.ProjectId));

        var globalResponse = await sessionClient.PostAsJsonAsync(
            "/api/admin/audit-log/export",
            new { projectId = (Guid?)null, limit = 100 });
        globalResponse.EnsureSuccessStatusCode();
        var global = await globalResponse.Content.ReadFromJsonAsync<AdminAuditLogExportResponse>();
        Assert.Contains(global!.Entries, entry => entry.EventId == firstEventId);
        Assert.Contains(global.Entries, entry => entry.EventId == secondEventId);
    }

    [Fact]
    public async Task Generate_admin_recovery_codes_with_session_cookie_requires_csrf_evidence()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.PostAsync("/api/admin/auth/recovery-codes", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_csrf.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Empty(dbContext.AdminRecoveryCodes);
        Assert.DoesNotContain(dbContext.AuditLogEntries, auditEntry => auditEntry.EventType == "admin.recovery_codes.generate");
    }

    [Fact]
    public async Task Generate_admin_recovery_codes_requires_recent_step_up_and_audits_denial()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var session = Assert.Single(dbContext.AdminSessions);
            session.StepUpAuthenticatedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
            await dbContext.SaveChangesAsync();
        }

        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var response = await sessionClient.PostAsync("/api/admin/auth/recovery-codes", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_step_up.required", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Empty(verifyDbContext.AdminRecoveryCodes);
        Assert.Contains(verifyDbContext.AuditLogEntries, auditEntry =>
            auditEntry.EventType == "admin.recovery_codes.generate" &&
            auditEntry.Outcome == "denied" &&
            auditEntry.ActorId == adminAccountId.ToString("D") &&
            auditEntry.SubjectType == "admin_account" &&
            auditEntry.SubjectId == adminAccountId.ToString("D") &&
            auditEntry.ReasonCode == "admin_step_up.required");
    }

    [Fact]
    public async Task Generate_admin_recovery_codes_returns_plain_codes_once_and_stores_only_hashes()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var response = await sessionClient.PostAsync("/api/admin/auth/recovery-codes", content: null);

        response.EnsureSuccessStatusCode();
        var recovery = await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>();
        Assert.NotEqual(DateTimeOffset.MinValue, recovery!.GeneratedAt);
        Assert.Equal(10, recovery.RecoveryCodes.Count);
        Assert.Equal(10, recovery.RecoveryCodes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(recovery.RecoveryCodes, code =>
        {
            Assert.Equal(14, code.Length);
            Assert.Equal('-', code[4]);
            Assert.Equal('-', code[9]);
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var storedCodes = dbContext.AdminRecoveryCodes.ToArray();
        Assert.Equal(10, storedCodes.Length);
        Assert.All(storedCodes, storedCode =>
        {
            Assert.Equal(adminAccountId, storedCode.AdminAccountId);
            Assert.Equal("active", storedCode.Status);
            Assert.StartsWith("pbkdf2-sha256:", storedCode.CodeHash, StringComparison.Ordinal);
            Assert.DoesNotContain(storedCode.CodeHash, recovery.RecoveryCodes, StringComparer.Ordinal);
        });
        Assert.Contains(dbContext.AuditLogEntries, auditEntry =>
            auditEntry.EventType == "admin.recovery_codes.generate" &&
            auditEntry.Outcome == "success" &&
            auditEntry.ActorId == adminAccountId.ToString("D") &&
            auditEntry.SubjectType == "admin_account" &&
            auditEntry.SubjectId == adminAccountId.ToString("D") &&
            auditEntry.ReasonCode == "admin_recovery_codes.generated");
    }

    [Fact]
    public async Task Generate_admin_recovery_codes_revokes_existing_active_codes_on_rotation()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);
        var firstResponse = await sessionClient.PostAsync("/api/admin/auth/recovery-codes", content: null);
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await sessionClient.PostAsync("/api/admin/auth/recovery-codes", content: null);

        secondResponse.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Equal(10, dbContext.AdminRecoveryCodes.Count(code => code.Status == "active"));
        Assert.Equal(10, dbContext.AdminRecoveryCodes.Count(code => code.Status == "revoked" && code.RevokedAt != null));
        Assert.Contains(dbContext.AuditLogEntries, auditEntry =>
            auditEntry.EventType == "admin.recovery_codes.revoke" &&
            auditEntry.Outcome == "revoked" &&
            auditEntry.ActorId == adminAccountId.ToString("D") &&
            auditEntry.SubjectType == "admin_account" &&
            auditEntry.SubjectId == adminAccountId.ToString("D") &&
            auditEntry.ReasonCode == "admin_recovery_codes.rotated");
    }

    [Fact]
    public async Task Resend_admin_webhook_delivery_requires_recent_step_up_and_audits_denial()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var session = Assert.Single(dbContext.AdminSessions);
            session.StepUpAuthenticatedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
            await dbContext.SaveChangesAsync();
        }

        var eventId = Guid.NewGuid();
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var response = await sessionClient.PostAsync(
            $"/api/admin/webhook-deliveries/{eventId}/resend?projectId={ProjectDefaults.DefaultProjectId:D}",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_step_up.required", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(verifyDbContext.AuditLogEntries, auditEntry =>
            auditEntry.EventType == "admin.webhook_delivery.resend" &&
            auditEntry.Outcome == "denied" &&
            auditEntry.ActorId == adminAccountId.ToString("D") &&
            auditEntry.SubjectType == "webhook_delivery" &&
            auditEntry.SubjectId == eventId.ToString("D") &&
            auditEntry.ReasonCode == "admin_step_up.required");
    }

    [Fact]
    public async Task Resend_admin_webhook_delivery_returns_not_found_and_audits_attempt()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var eventId = Guid.NewGuid();
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var response = await sessionClient.PostAsync(
            $"/api/admin/webhook-deliveries/{eventId}/resend?projectId={ProjectDefaults.DefaultProjectId:D}",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("webhook_delivery.not_found", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(verifyDbContext.AuditLogEntries, auditEntry =>
            auditEntry.EventType == "admin.webhook_delivery.resend" &&
            auditEntry.Outcome == "denied" &&
            auditEntry.ActorId == adminAccountId.ToString("D") &&
            auditEntry.SubjectType == "webhook_delivery" &&
            auditEntry.SubjectId == eventId.ToString("D") &&
            auditEntry.ReasonCode == "webhook_delivery.not_found");
    }

    [Fact]
    public async Task Get_admin_webhook_deliveries_requires_valid_admin_session()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/webhook-deliveries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_session.invalid", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_admin_webhook_deliveries_returns_resendable_delivery_summaries()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var credentialId = await factory.SeedCredentialAsync("integration-token");
        var now = DateTimeOffset.UtcNow;
        var paymentId = await SeedPaymentAsync(
            factory,
            credentialId,
            "failed-order",
            "waiting_for_payment",
            now.AddMinutes(-10));
        var resendableEventId = await SeedWebhookDeliveryAsync(
            factory,
            credentialId,
            paymentId,
            "retry_pending",
            "http.503",
            now.AddMinutes(-2));
        await SeedWebhookDeliveryAsync(
            factory,
            credentialId,
            paymentId,
            "delivered",
            lastErrorCode: null,
            now.AddMinutes(-1));
        using var signInClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(signInClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.GetAsync(
            $"/api/admin/webhook-deliveries?projectId={ProjectDefaults.DefaultProjectId:D}&limit=10");

        response.EnsureSuccessStatusCode();
        var webhookDeliveries = await response.Content.ReadFromJsonAsync<AdminWebhookDeliveriesResponse>();
        var delivery = Assert.Single(webhookDeliveries!.Deliveries);
        Assert.Equal(resendableEventId, delivery.WebhookEventId);
        Assert.Equal(paymentId, delivery.PaymentId);
        Assert.Equal("failed-order", delivery.PaymentExternalReference);
        Assert.Equal("payment.created", delivery.EventType);
        Assert.Equal("1", delivery.EventVersion);
        Assert.Equal("retry_pending", delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal("http.503", delivery.LastErrorCode);
        Assert.Equal("retry_pending", delivery.LastAttemptResult);
        Assert.Equal(503, delivery.LastHttpStatusCode);
        Assert.Equal("http.503", delivery.LastSafeErrorCode);
        Assert.NotNull(delivery.NextAttemptAt);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_admin_csrf_returns_request_token_and_secure_cookie()
    {
        await using var factory = new PaymentApiFactory();
        using var client = CreateHttpsClient(factory);

        var response = await client.GetAsync("/api/admin/csrf");

        response.EnsureSuccessStatusCode();
        var csrfResponse = await response.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.False(string.IsNullOrWhiteSpace(csrfResponse!.CsrfToken));
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
        var csrfCookie = Assert.Single(cookieHeaders, cookie => cookie.StartsWith("__Host-payaffe-admin-csrf=", StringComparison.Ordinal));
        Assert.Contains("httponly", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__Host-payaffe-admin=", csrfCookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_browser_api_does_not_enable_cross_origin_credentials_by_default()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/admin/session");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Logout_with_session_cookie_requires_csrf_evidence_and_keeps_session_active()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.PostAsync("/api/admin/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_csrf.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));

        var sessionResponse = await sessionClient.GetAsync("/api/admin/session");
        sessionResponse.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var session = Assert.Single(dbContext.AdminSessions);
        Assert.Null(session.RevokedAt);
        Assert.DoesNotContain(dbContext.AuditLogEntries, entry => entry.EventType == "admin.logout");
    }

    [Fact]
    public async Task Step_up_with_session_cookie_requires_csrf_evidence()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var response = await sessionClient.PostAsJsonAsync(
            "/api/admin/auth/step-up",
            new { totpCode = ComputeTotpCode(TotpSecret, DateTimeOffset.UtcNow) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_csrf.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.DoesNotContain(dbContext.AuditLogEntries, entry => entry.EventType == "admin.step_up");
    }

    [Fact]
    public async Task Step_up_updates_session_recency_and_audits_success()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var previousStepUp = DateTimeOffset.UtcNow.AddHours(-1);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var session = Assert.Single(dbContext.AdminSessions);
            session.StepUpAuthenticatedAt = previousStepUp;
            await dbContext.SaveChangesAsync();
        }

        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        // The sign-in used the current step, and a step is accepted once.
        var response = await sessionClient.PostAsJsonAsync(
            "/api/admin/auth/step-up",
            new { totpCode = NextTotpCode() });

        response.EnsureSuccessStatusCode();
        var stepUp = await response.Content.ReadFromJsonAsync<StepUpResponse>();
        Assert.Equal("step_up_authenticated", stepUp!.Status);
        Assert.True(stepUp.StepUpAuthenticatedAt > previousStepUp);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var steppedUpSession = Assert.Single(verifyDbContext.AdminSessions);
        Assert.Equal(stepUp.StepUpAuthenticatedAt, steppedUpSession.StepUpAuthenticatedAt);
        Assert.Equal(stepUp.IdleExpiresAt, steppedUpSession.IdleExpiresAt);
        Assert.Contains(verifyDbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.step_up" &&
            entry.Outcome == "success" &&
            entry.ActorId == adminAccountId.ToString("D") &&
            entry.SubjectType == "admin_session" &&
            entry.SubjectId == steppedUpSession.Id.ToString("D") &&
            entry.ReasonCode == "admin_step_up.verified");
    }

    [Fact]
    public async Task Step_up_returns_generic_error_and_audits_denial_for_invalid_code()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var response = await sessionClient.PostAsJsonAsync(
            "/api/admin/auth/step-up",
            new { totpCode = "000000" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_step_up.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("000000", body, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var session = Assert.Single(dbContext.AdminSessions);
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.step_up" &&
            entry.Outcome == "denied" &&
            entry.ActorId == adminAccountId.ToString("D") &&
            entry.SubjectId == session.Id.ToString("D") &&
            entry.ReasonCode == "admin_step_up.invalid");
    }

    /// <summary>
    /// With a skew of one step a code stays valid for about 90 seconds. Seen
    /// once, it must not work a second time within that window, neither for a
    /// new sign-in nor for step-up.
    /// </summary>
    [Fact]
    public async Task Totp_code_is_accepted_once_across_sign_in_and_step_up()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var code = ComputeTotpCode(TotpSecret, DateTimeOffset.UtcNow);
        var firstSignIn = await CompleteMfaAsync(client, code);
        firstSignIn.EnsureSuccessStatusCode();
        var rawSessionToken = ExtractCookieValue(Assert.Single(
            firstSignIn.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith("__Host-payaffe-admin=", StringComparison.Ordinal)));

        using var replayClient = factory.CreateClient();
        var replayedSignIn = await CompleteMfaAsync(replayClient, code);
        Assert.Equal(HttpStatusCode.Unauthorized, replayedSignIn.StatusCode);

        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);
        var replayedStepUp = await sessionClient.PostAsJsonAsync("/api/admin/auth/step-up", new { totpCode = code });
        Assert.Equal(HttpStatusCode.Unauthorized, replayedStepUp.StatusCode);

        var nextStepUp = await sessionClient.PostAsJsonAsync("/api/admin/auth/step-up", new { totpCode = NextTotpCode() });
        nextStepUp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Repeating the password step gives a fresh challenge with a fresh
    /// attempt cap, and a correct password must not clear the second-factor
    /// count either; only the account-level limit bounds the guessing.
    /// </summary>
    [Fact]
    public async Task Wrong_codes_across_fresh_challenges_lock_the_second_factor()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var wrongCode = WrongTotpCode();

        // Two wrong codes per challenge stays under the per-challenge cap of
        // five, so only the account-level count of ten can stop this.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await CompleteMfaAsync(client, wrongCode, newChallenge: attempt % 2 == 0);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var correctCode = await CompleteMfaAsync(client, ComputeTotpCode(TotpSecret, DateTimeOffset.UtcNow));
        Assert.Equal(HttpStatusCode.Unauthorized, correctCode.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Empty(dbContext.AdminSessions);
        var account = Assert.Single(dbContext.AdminAccounts);
        Assert.NotNull(account.SecondFactorLockedUntil);
        Assert.Equal(0, account.FailedPasswordAttemptCount);
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.second_factor.lock" &&
            entry.Outcome == "denied" &&
            entry.SubjectId == adminAccountId.ToString("D") &&
            entry.ReasonCode == "admin_second_factor.locked");
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.mfa_complete" &&
            entry.ReasonCode == "admin_mfa.second_factor_locked");
    }

    [Fact]
    public async Task Wrong_step_up_codes_lock_the_second_factor()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var rawSessionToken = ExtractCookieValue(await SignInAndGetSessionCookieAsync(client));
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);
        var wrongCode = WrongTotpCode();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await sessionClient.PostAsJsonAsync("/api/admin/auth/step-up", new { totpCode = wrongCode });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var correctCode = await sessionClient.PostAsJsonAsync("/api/admin/auth/step-up", new { totpCode = NextTotpCode() });
        Assert.Equal(HttpStatusCode.Unauthorized, correctCode.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.NotNull(Assert.Single(dbContext.AdminAccounts).SecondFactorLockedUntil);
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.step_up" &&
            entry.ReasonCode == "admin_step_up.second_factor_locked");
    }

    /// <summary>
    /// An answer that skips the key derivation comes back measurably sooner,
    /// which tells a caller the username does not exist or is locked.
    /// </summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("disabled")]
    [InlineData("locked")]
    public async Task Login_start_spends_a_password_check_on_accounts_it_refuses(string accountState)
    {
        var hasher = new CountingPasswordHasher();
        await using var baseFactory = new PaymentApiFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAdminPasswordHasher>();
                services.AddSingleton<IAdminPasswordHasher>(hasher);
            }));
        if (accountState != "unknown")
        {
            var adminAccountId = await baseFactory.SeedAdminAccountAsync(
                "admin@example.test",
                "correct-password",
                status: accountState == "disabled" ? "disabled" : "active");
            if (accountState == "locked")
            {
                using var scope = factory.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
                var account = dbContext.AdminAccounts.Single(candidate => candidate.Id == adminAccountId);
                account.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(10);
                await dbContext.SaveChangesAsync();
            }
        }

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new { username = "admin@example.test", password = "correct-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, hasher.VerifyCount);
    }

    [Fact]
    public async Task Logout_revokes_admin_session_and_clears_session_cookie()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(client);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var response = await sessionClient.PostAsync("/api/admin/auth/logout", content: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("""{"status":"logged_out"}""", body);
        Assert.DoesNotContain(rawSessionToken, body, StringComparison.Ordinal);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
        var clearedCookie = Assert.Single(cookieHeaders, cookie => cookie.StartsWith("__Host-payaffe-admin=", StringComparison.Ordinal));
        Assert.Contains("expires=", clearedCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", clearedCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", clearedCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", clearedCookie, StringComparison.OrdinalIgnoreCase);

        var sessionResponse = await sessionClient.GetAsync("/api/admin/session");
        Assert.Equal(HttpStatusCode.Unauthorized, sessionResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var session = Assert.Single(dbContext.AdminSessions);
        Assert.NotNull(session.RevokedAt);
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.logout" &&
            entry.Outcome == "success" &&
            entry.ActorId == adminAccountId.ToString("D") &&
            entry.SubjectType == "admin_session" &&
            entry.SubjectId == session.Id.ToString("D") &&
            entry.ReasonCode == "admin_logout.session_revoked");
    }

    [Fact]
    public async Task Logout_without_session_cookie_returns_generic_success_and_clears_cookie()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/admin/auth/logout", content: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("""{"status":"logged_out"}""", body);
        Assert.DoesNotContain("__Host-payaffe-admin", body, StringComparison.Ordinal);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
        Assert.Single(cookieHeaders, cookie => cookie.StartsWith("__Host-payaffe-admin=", StringComparison.Ordinal));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Empty(dbContext.AdminSessions);
        Assert.Empty(dbContext.AuditLogEntries);
    }

    [Fact]
    public async Task Mfa_complete_returns_generic_error_for_invalid_code_and_does_not_create_session()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "admin@example.test",
                password = "correct-password",
            });
        var loginStart = await loginResponse.Content.ReadFromJsonAsync<LoginStartResponse>();

        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/mfa",
            new
            {
                challengeId = loginStart!.ChallengeId,
                totpCode = "000000",
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_mfa.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("000000", body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Empty(dbContext.AdminSessions);
        var challenge = Assert.Single(dbContext.AdminLoginChallenges);
        Assert.Equal(1, challenge.FailedAttemptCount);
        Assert.Null(challenge.ConsumedAt);
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "admin.mfa_complete" &&
            entry.Outcome == "failure" &&
            entry.ReasonCode == "admin_mfa.invalid");
    }

    [Fact]
    public async Task Login_start_locks_account_after_repeated_invalid_passwords()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync("admin@example.test", "correct-password");
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/admin/auth/login",
                new
                {
                    username = "admin@example.test",
                    password = "wrong-password",
                });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var adminAccount = Assert.Single(dbContext.AdminAccounts);
        Assert.Equal(5, adminAccount.FailedPasswordAttemptCount);
        Assert.NotNull(adminAccount.LockedUntil);
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.Outcome == "denied" && entry.ReasonCode == "admin_login.account_locked");
    }

    [Fact]
    public async Task Login_start_returns_same_generic_error_for_unknown_account()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "missing@example.test",
                password = "any-password",
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin_login.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("missing@example.test", body, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var auditEntry = Assert.Single(dbContext.AuditLogEntries);
        Assert.Equal("system", auditEntry.ActorType);
        Assert.Equal("unknown", auditEntry.SubjectId);
        Assert.Equal("admin_login.invalid_credentials", auditEntry.ReasonCode);
    }

    /// <summary>
    /// The code for the next time step, still within the allowed skew. Used
    /// where the current step was already spent on the sign-in.
    /// </summary>
    private static string NextTotpCode() => ComputeTotpCode(TotpSecret, DateTimeOffset.UtcNow.AddSeconds(30));

    /// <summary>A six-digit code valid for none of the steps within the skew.</summary>
    private static string WrongTotpCode()
    {
        var now = DateTimeOffset.UtcNow;
        var valid = new[] { -60, -30, 0, 30, 60 }
            .Select(offset => ComputeTotpCode(TotpSecret, now.AddSeconds(offset)))
            .ToHashSet(StringComparer.Ordinal);
        return Enumerable.Range(0, 10)
            .Select(digit => new string((char)('0' + digit), 6))
            .First(candidate => !valid.Contains(candidate));
    }

    private Guid? _pendingChallengeId;

    private async Task<HttpResponseMessage> CompleteMfaAsync(
        HttpClient client,
        string totpCode,
        bool newChallenge = true)
    {
        if (newChallenge || _pendingChallengeId is null)
        {
            var loginResponse = await client.PostAsJsonAsync(
                "/api/admin/auth/login",
                new { username = "admin@example.test", password = "correct-password" });
            loginResponse.EnsureSuccessStatusCode();
            _pendingChallengeId = (await loginResponse.Content.ReadFromJsonAsync<LoginStartResponse>())!.ChallengeId;
        }

        return await client.PostAsJsonAsync(
            "/api/admin/auth/mfa",
            new { challengeId = _pendingChallengeId, totpCode });
    }

    private sealed class CountingPasswordHasher : IAdminPasswordHasher
    {
        private readonly AdminPasswordHasher _inner = new();
        private int _verifyCount;

        public int VerifyCount => _verifyCount;

        public string HashPassword(string password) => _inner.HashPassword(password);

        public bool VerifyPassword(string password, string passwordHash)
        {
            Interlocked.Increment(ref _verifyCount);
            return _inner.VerifyPassword(password, passwordHash);
        }
    }

    private static string ComputeTotpCode(byte[] secret, DateTimeOffset now)
    {
        var timeStep = now.ToUnixTimeSeconds() / 30;
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0f;
        var binaryCode = ((hash[offset] & 0x7f) << 24)
            | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8)
            | (hash[offset + 3] & 0xff);
        return (binaryCode % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Create_and_list_integration_api_credential_returns_token_once_and_audits()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var projectId = await factory.SeedProjectAsync();
        using var loginClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(loginClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var createResponse = await sessionClient.PostAsJsonAsync(
            "/api/admin/integration-api-credentials",
            new { projectId, name = " Partner production " });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<AdminIntegrationApiCredentialSecretResponse>();
        Assert.NotNull(created);
        Assert.Equal(projectId, created.Credential.ProjectId);
        Assert.Equal("Partner production", created.Credential.Name);
        Assert.Equal("active", created.Credential.Status);
        Assert.Equal(1, created.Credential.Version);
        Assert.StartsWith("payaffe_integration_", created.Token, StringComparison.Ordinal);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var stored = Assert.Single(dbContext.IntegrationApiCredentials);
            Assert.Equal(projectId, stored.ProjectId);
            Assert.Equal(created.Credential.Id, stored.Id);
            Assert.Equal(IntegrationApiCredentialTokenHasher.HashToken(created.Token), stored.TokenHash);
            Assert.DoesNotContain(created.Token, stored.TokenHash, StringComparison.Ordinal);
            Assert.Contains(dbContext.AuditLogEntries, audit =>
                audit.EventType == "admin.integration_api_credential.create" &&
                audit.Outcome == "success" &&
                audit.ProjectId == projectId &&
                audit.ActorId == adminAccountId.ToString("D") &&
                audit.ReasonCode == "integration_api_credential.created" &&
                audit.SubjectId == stored.Id.ToString("D"));
        }

        var listResponse = await sessionClient.GetAsync(
            $"/api/admin/integration-api-credentials?projectId={projectId:D}");
        listResponse.EnsureSuccessStatusCode();
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.Token, listBody, StringComparison.Ordinal);
        var listed = await listResponse.Content
            .ReadFromJsonAsync<AdminIntegrationApiCredentialsResponse>();
        var credential = Assert.Single(listed!.Credentials);
        Assert.Equal(created.Credential.Id, credential.Id);
        Assert.Equal(projectId, credential.ProjectId);
        Assert.Equal("Partner production", credential.Name);
    }

    [Fact]
    public async Task Create_integration_api_credential_requires_csrf_and_recent_step_up()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var loginClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(loginClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");

        var csrfResponse = await sessionClient.PostAsJsonAsync(
            "/api/admin/integration-api-credentials",
            new { name = "Partner" });

        Assert.Equal(HttpStatusCode.Forbidden, csrfResponse.StatusCode);
        Assert.Contains(
            "admin_csrf.invalid",
            await csrfResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var session = Assert.Single(dbContext.AdminSessions);
            session.StepUpAuthenticatedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
            await dbContext.SaveChangesAsync();
        }

        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var steppedOutClient = CreateHttpsClient(factory);
        steppedOutClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        steppedOutClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);
        var stepUpResponse = await steppedOutClient.PostAsJsonAsync(
            "/api/admin/integration-api-credentials",
            new { name = "Partner" });

        Assert.Equal(HttpStatusCode.Forbidden, stepUpResponse.StatusCode);
        Assert.Contains(
            "admin_step_up.required",
            await stepUpResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Empty(verifyDbContext.IntegrationApiCredentials);
        Assert.Contains(verifyDbContext.AuditLogEntries, audit =>
            audit.EventType == "admin.integration_api_credential.create" &&
            audit.Outcome == "denied" &&
            audit.ActorId == adminAccountId.ToString("D") &&
            audit.ReasonCode == "admin_step_up.required");
    }

    [Fact]
    public async Task Rotate_integration_api_credential_revokes_old_token_and_returns_new_token_once()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var credentialId = await factory.SeedCredentialAsync("old-partner-token");
        using var loginClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(loginClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var rotateResponse = await sessionClient.PostAsJsonAsync(
            $"/api/admin/integration-api-credentials/{credentialId:D}/rotate",
            new { projectId = ProjectDefaults.DefaultProjectId, expectedVersion = 1 });

        rotateResponse.EnsureSuccessStatusCode();
        var rotated = await rotateResponse.Content
            .ReadFromJsonAsync<AdminIntegrationApiCredentialSecretResponse>();
        Assert.NotNull(rotated);
        Assert.Equal(ProjectDefaults.DefaultProjectId, rotated.Credential.ProjectId);
        Assert.Equal(credentialId, rotated.Credential.Id);
        Assert.Equal(2, rotated.Credential.Version);
        Assert.StartsWith("payaffe_integration_", rotated.Token, StringComparison.Ordinal);
        Assert.NotEqual("old-partner-token", rotated.Token);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var stored = Assert.Single(dbContext.IntegrationApiCredentials);
            Assert.Equal(IntegrationApiCredentialTokenHasher.HashToken(rotated.Token), stored.TokenHash);
            Assert.Contains(dbContext.AuditLogEntries, audit =>
                audit.EventType == "admin.integration_api_credential.rotate" &&
                audit.Outcome == "success" &&
                audit.ProjectId == ProjectDefaults.DefaultProjectId &&
                audit.SubjectId == credentialId.ToString("D"));
        }

        using var oldTokenClient = factory.CreateClient();
        oldTokenClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old-partner-token");
        oldTokenClient.DefaultRequestHeaders.Add("Idempotency-Key", "old-token-request");
        var oldTokenResponse = await oldTokenClient.PostAsJsonAsync(
            "/api/v1/payments",
            CreatePaymentRequest("old-token-payment"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldTokenResponse.StatusCode);

        using var newTokenClient = factory.CreateClient();
        newTokenClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", rotated.Token);
        newTokenClient.DefaultRequestHeaders.Add("Idempotency-Key", "new-token-request");
        var newTokenResponse = await newTokenClient.PostAsJsonAsync(
            "/api/v1/payments",
            CreatePaymentRequest("new-token-payment"));
        Assert.Equal(HttpStatusCode.Created, newTokenResponse.StatusCode);
    }

    [Fact]
    public async Task Credential_mutations_enforce_expected_version_and_disable_authentication()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var credentialId = await factory.SeedCredentialAsync("partner-token");
        using var loginClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(loginClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var conflictResponse = await sessionClient.PostAsJsonAsync(
            $"/api/admin/integration-api-credentials/{credentialId:D}/disable",
            new { projectId = ProjectDefaults.DefaultProjectId, expectedVersion = 7 });

        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        var conflictBody = await conflictResponse.Content.ReadAsStringAsync();
        Assert.Contains("integration_api_credential.concurrency_conflict", conflictBody, StringComparison.Ordinal);
        Assert.Contains("\"currentVersion\":1", conflictBody, StringComparison.Ordinal);

        var disableResponse = await sessionClient.PostAsJsonAsync(
            $"/api/admin/integration-api-credentials/{credentialId:D}/disable",
            new { projectId = ProjectDefaults.DefaultProjectId, expectedVersion = 1 });

        disableResponse.EnsureSuccessStatusCode();
        var disabled = await disableResponse.Content
            .ReadFromJsonAsync<AdminIntegrationApiCredentialResponse>();
        Assert.Equal("disabled", disabled!.Credential.Status);
        Assert.Equal(ProjectDefaults.DefaultProjectId, disabled.Credential.ProjectId);
        Assert.Equal(2, disabled.Credential.Version);

        using var integrationClient = factory.CreateClient();
        integrationClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "partner-token");
        integrationClient.DefaultRequestHeaders.Add("Idempotency-Key", "disabled-token-request");
        var integrationResponse = await integrationClient.PostAsJsonAsync(
            "/api/v1/payments",
            CreatePaymentRequest("disabled-token-payment"));
        Assert.Equal(HttpStatusCode.Unauthorized, integrationResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(dbContext.AuditLogEntries, audit =>
            audit.EventType == "admin.integration_api_credential.disable" &&
            audit.Outcome == "success" &&
            audit.ProjectId == ProjectDefaults.DefaultProjectId &&
            audit.ReasonCode == "integration_api_credential.disabled" &&
            audit.SubjectId == credentialId.ToString("D"));
    }

    [Fact]
    public async Task Admin_manages_webhook_endpoint_configuration_with_secret_references_and_audit()
    {
        await using var factory = new PaymentApiFactory();
        var projectId = await factory.SeedProjectAsync();
        var firstSecretReference = $"configuration:Webhooks:Projects:{projectId:D}:EndpointSecrets:partner-v1";
        var secondSecretReference = $"configuration:Webhooks:Projects:{projectId:D}:EndpointSecrets:partner-v2";
        factory.AddWebhookSecret(firstSecretReference, "first-webhook-secret");
        factory.AddWebhookSecret(secondSecretReference, "second-webhook-secret");
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var credentialId = await factory.SeedCredentialAsync("partner-token", projectId: projectId);
        using var loginClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(loginClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var createResponse = await sessionClient.PostAsJsonAsync(
            "/api/admin/webhook-endpoints",
            new
            {
                projectId,
                integrationApiCredentialId = credentialId,
                url = "https://partner.example.test/payaffe",
                secretReference = firstSecretReference,
                eventTypes = new[] { "payment.completed", "payment.created", "payment.completed" },
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<AdminWebhookEndpointResponse>();
        Assert.NotNull(created);
        Assert.Equal(projectId, created.Endpoint.ProjectId);
        Assert.Equal(credentialId, created.Endpoint.IntegrationApiCredentialId);
        Assert.Equal(firstSecretReference, created.Endpoint.SecretReference);
        Assert.Equal(["payment.completed", "payment.created"], created.Endpoint.EventTypes);
        Assert.Equal(1, created.Endpoint.Version);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("first-webhook-secret", createBody, StringComparison.Ordinal);

        var updateResponse = await sessionClient.PostAsJsonAsync(
            $"/api/admin/webhook-endpoints/{created.Endpoint.Id:D}/update",
            new
            {
                projectId,
                expectedVersion = 1,
                url = "https://partner.example.test/hooks/payments",
                eventTypes = Array.Empty<string>(),
            });
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<AdminWebhookEndpointResponse>();
        Assert.Equal("https://partner.example.test/hooks/payments", updated!.Endpoint.Url);
        Assert.Empty(updated.Endpoint.EventTypes);
        Assert.Equal(2, updated.Endpoint.Version);

        var rotateResponse = await sessionClient.PostAsJsonAsync(
            $"/api/admin/webhook-endpoints/{created.Endpoint.Id:D}/rotate-secret",
            new
            {
                projectId,
                expectedVersion = 2,
                secretReference = secondSecretReference,
            });
        rotateResponse.EnsureSuccessStatusCode();
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<AdminWebhookEndpointResponse>();
        Assert.Equal(secondSecretReference, rotated!.Endpoint.SecretReference);
        Assert.Equal(3, rotated.Endpoint.Version);
        Assert.DoesNotContain(
            "second-webhook-secret",
            await rotateResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        var listResponse = await sessionClient.GetAsync(
            $"/api/admin/webhook-endpoints?projectId={projectId:D}&integrationApiCredentialId={credentialId:D}");
        listResponse.EnsureSuccessStatusCode();
        var listed = await listResponse.Content.ReadFromJsonAsync<AdminWebhookEndpointsResponse>();
        Assert.Equal(created.Endpoint.Id, Assert.Single(listed!.Endpoints).Id);

        var disableResponse = await sessionClient.PostAsJsonAsync(
            $"/api/admin/webhook-endpoints/{created.Endpoint.Id:D}/disable",
            new { projectId, expectedVersion = 3 });
        disableResponse.EnsureSuccessStatusCode();
        var disabled = await disableResponse.Content.ReadFromJsonAsync<AdminWebhookEndpointResponse>();
        Assert.Equal("disabled", disabled!.Endpoint.Status);
        Assert.Equal(4, disabled.Endpoint.Version);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var stored = Assert.Single(dbContext.WebhookEndpoints);
        Assert.Equal(projectId, stored.ProjectId);
        Assert.Equal(secondSecretReference, stored.SecretReference);
        Assert.NotEqual("second-webhook-secret", stored.SecretReference);
        Assert.Equal("disabled", stored.Status);
        Assert.All(
            new[]
            {
                "admin.webhook_endpoint.create",
                "admin.webhook_endpoint.update",
                "admin.webhook_endpoint.rotate_secret",
                "admin.webhook_endpoint.disable",
            },
            eventType => Assert.Contains(dbContext.AuditLogEntries, audit =>
                audit.EventType == eventType &&
                audit.ProjectId == projectId &&
                audit.Outcome == "success" &&
                audit.SubjectId == stored.Id.ToString("D")));
    }

    [Fact]
    public async Task Webhook_endpoint_creation_rejects_unavailable_secret_and_disabled_credential()
    {
        await using var factory = new PaymentApiFactory();
        factory.AddWebhookSecret(
            "configuration:Webhooks:EndpointSecrets:configured",
            "configured-secret");
        await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var credentialId = await factory.SeedCredentialAsync("disabled-partner-token", status: "disabled");
        using var loginClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(loginClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var missingSecretResponse = await sessionClient.PostAsJsonAsync(
            "/api/admin/webhook-endpoints",
            new
            {
                projectId = ProjectDefaults.DefaultProjectId,
                integrationApiCredentialId = credentialId,
                url = "https://partner.example.test/hooks",
                secretReference = "configuration:Webhooks:EndpointSecrets:missing",
                eventTypes = Array.Empty<string>(),
            });
        Assert.Equal(HttpStatusCode.Conflict, missingSecretResponse.StatusCode);
        Assert.Contains(
            "webhook_endpoint.secret_unavailable",
            await missingSecretResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        var disabledCredentialResponse = await sessionClient.PostAsJsonAsync(
            "/api/admin/webhook-endpoints",
            new
            {
                projectId = ProjectDefaults.DefaultProjectId,
                integrationApiCredentialId = credentialId,
                url = "https://partner.example.test/hooks",
                secretReference = "configuration:Webhooks:EndpointSecrets:configured",
                eventTypes = Array.Empty<string>(),
            });
        Assert.Equal(HttpStatusCode.Conflict, disabledCredentialResponse.StatusCode);
        Assert.Contains(
            "webhook_endpoint.integration_api_credential_unavailable",
            await disabledCredentialResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Empty(dbContext.WebhookEndpoints);
    }

    private static object CreatePaymentRequest(string externalReference) =>
        new
        {
            fiatCurrency = "EUR",
            fiatAmountMinor = 1999,
            externalReference,
        };

    private static async Task<Guid> SeedRecoveryCodeAsync(
        PaymentApiFactory factory,
        Guid adminAccountId,
        string recoveryCode)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var now = DateTimeOffset.UtcNow;
        var recoveryCodeId = Guid.NewGuid();
        var passwordHasher = new AdminPasswordHasher();
        dbContext.AdminRecoveryCodes.Add(new AdminRecoveryCodeRecord
        {
            Id = recoveryCodeId,
            AdminAccountId = adminAccountId,
            CodeHash = passwordHasher.HashPassword(recoveryCode),
            Status = "active",
            CreatedAt = now,
        });

        await dbContext.SaveChangesAsync();
        return recoveryCodeId;
    }

    [Fact]
    public async Task Import_native_eth_address_pool_validates_deduplicates_reports_capacity_and_audits()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        using var loginClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(loginClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);
        const string firstAddress = "0x1111111111111111111111111111111111111111";
        const string secondAddress = "0x2222222222222222222222222222222222222222";

        var importResponse = await sessionClient.PostAsJsonAsync(
            "/api/admin/native-eth-address-pool/import",
            new
            {
                projectId = ProjectDefaults.DefaultProjectId,
                addresses = new[] { firstAddress.ToUpperInvariant(), secondAddress },
            });

        Assert.Equal(HttpStatusCode.Created, importResponse.StatusCode);
        var imported = await importResponse.Content
            .ReadFromJsonAsync<AdminNativeEthAddressPoolImportResponse>();
        Assert.NotNull(imported);
        Assert.Equal(2, imported.ImportedCount);
        Assert.Equal(2, imported.Summary.UnusedCount);
        Assert.True(imported.Summary.IsLowCapacity);

        var duplicateResponse = await sessionClient.PostAsJsonAsync(
            "/api/admin/native-eth-address-pool/import",
            new { projectId = ProjectDefaults.DefaultProjectId, addresses = new[] { firstAddress } });
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Contains(
            "native_eth_address_pool.duplicate",
            await duplicateResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        var summaryResponse = await sessionClient.GetAsync(
            $"/api/admin/native-eth-address-pool?projectId={ProjectDefaults.DefaultProjectId:D}");
        summaryResponse.EnsureSuccessStatusCode();
        var summary = await summaryResponse.Content
            .ReadFromJsonAsync<AdminNativeEthAddressPoolSummaryResponse>();
        Assert.Equal(2, summary!.UnusedCount);
        Assert.Equal(0, summary.AssignedCount);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Equal(2, dbContext.NativeEthAddresses.Count());
        Assert.All(dbContext.NativeEthAddresses, address =>
            Assert.Equal(address.Address.ToLowerInvariant(), address.Address));
        Assert.Contains(dbContext.AuditLogEntries, audit =>
            audit.EventType == "admin.native_eth_address_pool.import" &&
            audit.ActorId == adminAccountId.ToString("D") &&
            audit.ReasonCode == "native_eth_address_pool.imported");
    }

    [Fact]
    public async Task Manual_settlement_persists_payment_event_webhook_and_audit_atomically()
    {
        await using var factory = new PaymentApiFactory();
        var adminAccountId = await factory.SeedAdminAccountAsync(
            "admin@example.test",
            "correct-password",
            totpSecret: TotpSecret);
        var paymentId = Guid.NewGuid();
        using (var setupScope = factory.Services.CreateScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var now = DateTimeOffset.UtcNow;
            var credentialId = Guid.NewGuid();
            dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
            {
                Id = credentialId,
                Name = "Settlement test",
                TokenHash = IntegrationApiCredentialTokenHasher.HashToken("settlement-token"),
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
            dbContext.Payments.Add(new PaymentRecord
            {
                Id = paymentId,
                IntegrationApiCredentialId = credentialId,
                ExternalReference = "late-payment",
                FiatCurrency = "EUR",
                FiatAmountMinor = 1999,
                Status = "expired",
                PayerPageId = "settlement-payer-page",
                ExpiresAt = now.AddHours(-2),
                LateAcceptanceEndsAt = now.AddHours(-1),
                SelectedCurrency = "BTC",
                ExpectedCryptoAmount = "0.0003998",
                PaymentAddress = "btc-settlement-address",
                CreatedAt = now.AddHours(-3),
                UpdatedAt = now,
                Version = 1,
            });
            dbContext.MatchingBlockchainTransactions.Add(new MatchingBlockchainTransactionRecord
            {
                Id = Guid.NewGuid(),
                PaymentId = paymentId,
                SupportedCurrency = "BTC",
                PaymentAddress = "btc-settlement-address",
                TransactionHash = "late-transaction",
                ObservedAmount = "0.0003",
                ObservedAt = now,
                FirstObservedAt = now,
                Confirmations = 1,
                ProviderName = "test",
                LastCheckedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
            await dbContext.SaveChangesAsync();
        }

        using var loginClient = factory.CreateClient();
        var sessionCookie = await SignInAndGetSessionCookieAsync(loginClient);
        var rawSessionToken = ExtractCookieValue(sessionCookie);
        var csrf = await GetAdminCsrfAsync(factory, rawSessionToken);
        using var sessionClient = CreateHttpsClient(factory);
        sessionClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-payaffe-admin={rawSessionToken}; {csrf.CookiePair}");
        sessionClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var response = await sessionClient.PostAsJsonAsync(
            $"/api/admin/payments/{paymentId:D}/settle",
            new
            {
                projectId = ProjectDefaults.DefaultProjectId,
                expectedVersion = 1,
                reason = "Late payment verified by operator.",
            });

        response.EnsureSuccessStatusCode();
        var settled = await response.Content.ReadFromJsonAsync<AdminPaymentDetailResponse>();
        Assert.Equal("settled", settled!.Status);
        Assert.NotNull(settled.SettledAt);
        Assert.Equal(2, settled.Version);

        using var integrationClient = factory.CreateClient();
        integrationClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "settlement-token");
        var pollingResponse = await integrationClient.GetAsync($"/api/v1/payments/{paymentId:D}");
        pollingResponse.EnsureSuccessStatusCode();
        var pollingPayment = await pollingResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("settled", pollingPayment.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, pollingPayment.GetProperty("settledAt").ValueKind);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var payment = Assert.Single(verifyDbContext.Payments);
        Assert.Equal("settled", payment.Status);
        Assert.NotNull(payment.SettledAt);
        Assert.Contains(verifyDbContext.PaymentEventHistory, paymentEvent =>
            paymentEvent.PaymentId == paymentId &&
            paymentEvent.EventType == "payment.settled");
        Assert.Contains(verifyDbContext.WebhookOutboxEvents, webhook =>
            webhook.PaymentId == paymentId &&
            webhook.EventType == "payment.settled" &&
            webhook.Status == "pending");
        Assert.Contains(verifyDbContext.AuditLogEntries, audit =>
            audit.EventType == "admin.payment.settle" &&
            audit.ActorId == adminAccountId.ToString("D") &&
            audit.SubjectId == paymentId.ToString("D"));
    }

    private static string ExtractCookieValue(string setCookieHeader)
    {
        var start = setCookieHeader.IndexOf('=', StringComparison.Ordinal) + 1;
        var end = setCookieHeader.IndexOf(';', start);
        return end < 0 ? setCookieHeader[start..] : setCookieHeader[start..end];
    }

    private static string ExtractCookiePair(string setCookieHeader)
    {
        var end = setCookieHeader.IndexOf(';', StringComparison.Ordinal);
        return end < 0 ? setCookieHeader : setCookieHeader[..end];
    }

    private static async Task<string> SignInAndGetSessionCookieAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new
            {
                username = "admin@example.test",
                password = "correct-password",
            });
        var loginStart = await loginResponse.Content.ReadFromJsonAsync<LoginStartResponse>();
        var mfaResponse = await client.PostAsJsonAsync(
            "/api/admin/auth/mfa",
            new
            {
                challengeId = loginStart!.ChallengeId,
                totpCode = ComputeTotpCode(TotpSecret, DateTimeOffset.UtcNow),
            });
        mfaResponse.EnsureSuccessStatusCode();
        Assert.True(mfaResponse.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
        return Assert.Single(cookieHeaders, cookie => cookie.StartsWith("__Host-payaffe-admin=", StringComparison.Ordinal));
    }

    private static async Task<Guid> SeedPaymentAsync(
        PaymentApiFactory factory,
        Guid integrationApiCredentialId,
        string externalReference,
        string status,
        DateTimeOffset createdAt,
        string? selectedCurrency = null,
        string? expectedCryptoAmount = null,
        string? paymentAddress = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var paymentId = Guid.NewGuid();
        dbContext.Payments.Add(new PaymentRecord
        {
            Id = paymentId,
            IntegrationApiCredentialId = integrationApiCredentialId,
            ExternalReference = externalReference,
            FiatCurrency = "EUR",
            FiatAmountMinor = 1999,
            Status = status,
            PayerPageId = $"payer-{paymentId:N}",
            ExpiresAt = createdAt.AddHours(1),
            LateAcceptanceEndsAt = createdAt.AddHours(25),
            SelectedCurrency = selectedCurrency,
            ExpectedCryptoAmount = expectedCryptoAmount,
            PaymentAddress = paymentAddress,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
        await dbContext.SaveChangesAsync();

        return paymentId;
    }

    private static async Task SeedObservationAsync(
        PaymentApiFactory factory,
        Guid paymentId,
        string observedAmount,
        DateTimeOffset observedAt)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.MatchingBlockchainTransactions.Add(new MatchingBlockchainTransactionRecord
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            SupportedCurrency = "BTC",
            PaymentAddress = "btc-test-address",
            TransactionHash = $"tx-{Guid.NewGuid():N}",
            ObservedAmount = observedAmount,
            ObservedAt = observedAt,
            FirstObservedAt = observedAt,
            Confirmations = 0,
            ProviderName = "test-provider",
            CreatedAt = observedAt,
            UpdatedAt = observedAt,
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<Guid> SeedWebhookDeliveryAsync(
        PaymentApiFactory factory,
        Guid integrationApiCredentialId,
        Guid paymentId,
        string status,
        string? lastErrorCode,
        DateTimeOffset occurredAt)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var endpointId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        dbContext.WebhookEndpoints.Add(new WebhookEndpointRecord
        {
            Id = endpointId,
            IntegrationApiCredentialId = integrationApiCredentialId,
            Url = "https://receiver.example.test/webhooks/payaffe",
            SecretReference = "secret://webhooks/test",
            Status = "active",
            CreatedAt = occurredAt.AddMinutes(-1),
            UpdatedAt = occurredAt.AddMinutes(-1),
        });
        dbContext.WebhookOutboxEvents.Add(new WebhookOutboxEventRecord
        {
            Id = eventId,
            PaymentId = paymentId,
            IntegrationApiCredentialId = integrationApiCredentialId,
            EventType = "payment.created",
            EventVersion = "1",
            PayloadVersion = 1,
            ResourceType = "payment",
            ResourceId = paymentId.ToString("D"),
            Status = status,
            OccurredAt = occurredAt,
            CreatedAt = occurredAt,
            NextAttemptAt = occurredAt.AddMinutes(5),
            AttemptCount = status == "delivered" ? 0 : 1,
            LastErrorCode = lastErrorCode,
            CorrelationId = $"test-{eventId:N}",
        });

        if (status != "delivered")
        {
            dbContext.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttemptRecord
            {
                Id = Guid.NewGuid(),
                WebhookEventId = eventId,
                WebhookEndpointId = endpointId,
                AttemptNumber = 1,
                AttemptedAt = occurredAt.AddMinutes(1),
                Result = status,
                HttpStatusCode = 503,
                SafeErrorCode = lastErrorCode,
                NextRetryAt = occurredAt.AddMinutes(5),
                CorrelationId = $"test-{eventId:N}",
            });
        }

        await dbContext.SaveChangesAsync();
        return eventId;
    }

    private static async Task<Guid> SeedAuditEntryAsync(
        PaymentApiFactory factory,
        DateTimeOffset occurredAt,
        string eventType,
        string outcome,
        Guid adminAccountId,
        string reasonCode,
        string? sourceIp = null,
        string? userAgent = null,
        Guid? projectId = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var eventId = Guid.NewGuid();
        dbContext.AuditLogEntries.Add(new AuditLogEntryRecord
        {
            ProjectId = projectId,
            EventId = eventId,
            OccurredAt = occurredAt,
            EventType = eventType,
            Outcome = outcome,
            ActorType = "product_user",
            ActorId = adminAccountId.ToString("D"),
            SourceService = "api",
            SourceIp = sourceIp,
            UserAgent = userAgent,
            CorrelationId = $"test-{eventId:N}",
            ReasonCode = reasonCode,
            SubjectType = "admin_session",
            SubjectId = "recent-session",
        });
        await dbContext.SaveChangesAsync();
        return eventId;
    }

    private static async Task<(string Token, string CookiePair)> GetAdminCsrfAsync(
        PaymentApiFactory factory,
        string rawSessionToken)
    {
        using var client = CreateHttpsClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-payaffe-admin={rawSessionToken}");
        var response = await client.GetAsync("/api/admin/csrf");
        response.EnsureSuccessStatusCode();
        var csrfResponse = await response.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
        var csrfCookie = Assert.Single(cookieHeaders, cookie => cookie.StartsWith("__Host-payaffe-admin-csrf=", StringComparison.Ordinal));
        return (csrfResponse!.CsrfToken, ExtractCookiePair(csrfCookie));
    }

    private static HttpClient CreateHttpsClient(PaymentApiFactory factory)
    {
        var client = factory.CreateClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    // Nullable since ADR 0028: a password-only sign-in returns no challenge.
    private sealed record LoginStartResponse(string Status, Guid? ChallengeId);

    private sealed record CsrfResponse(string CsrfToken);

    private sealed record SessionResponse(
        string Status,
        Guid AdminAccountId,
        string Username,
        DateTimeOffset MfaAuthenticatedAt,
        DateTimeOffset StepUpAuthenticatedAt,
        DateTimeOffset ExpiresAt,
        DateTimeOffset IdleExpiresAt);

    private sealed record StepUpResponse(
        string Status,
        DateTimeOffset StepUpAuthenticatedAt,
        DateTimeOffset IdleExpiresAt);

    private sealed record AdminPaymentsResponse(IReadOnlyList<AdminPaymentSummaryResponse> Payments);

    private sealed record AdminProjectsResponse(IReadOnlyList<AdminProjectResponseModel> Projects);

    private sealed record AdminProjectResponse(AdminProjectResponseModel Project);

    private sealed record AdminProjectResponseModel(
        Guid ProjectId,
        string Name,
        string Slug,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        long Version);

    private sealed record AdminAuditLogResponse(IReadOnlyList<AdminAuditLogEntryResponse> Entries);

    private sealed record AdminWebhookDeliveriesResponse(IReadOnlyList<AdminWebhookDeliveryResponse> Deliveries);

    private sealed record AdminPaymentSummaryResponse(
        Guid PaymentId,
        string ExternalReference,
        string FiatCurrency,
        long FiatAmountMinor,
        string Status,
        string? SelectedCurrency,
        string? ExpectedCryptoAmount,
        string? PaymentAddress,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record AdminAuditLogEntryResponse(
        Guid? ProjectId,
        Guid EventId,
        DateTimeOffset OccurredAt,
        string EventType,
        string Outcome,
        string ActorType,
        string ActorId,
        string ReasonCode,
        string SubjectType,
        string SubjectId);

    private sealed record AdminAuditLogEntryDetailResponse(
        Guid? ProjectId,
        Guid EventId,
        DateTimeOffset OccurredAt,
        string EventType,
        string Outcome,
        string ActorType,
        string ActorId,
        string SourceService,
        string? SourceIp,
        string? UserAgent,
        string CorrelationId,
        string ReasonCode,
        string SubjectType,
        string SubjectId);

    private sealed record AdminWebhookDeliveryResponse(
        Guid WebhookEventId,
        Guid PaymentId,
        string PaymentExternalReference,
        string EventType,
        string EventVersion,
        string Status,
        int AttemptCount,
        string? LastErrorCode,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset OccurredAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastAttemptedAt,
        string? LastAttemptResult,
        int? LastHttpStatusCode,
        string? LastSafeErrorCode,
        string CorrelationId);

    private sealed record AdminAuditLogExportResponse(
        DateTimeOffset ExportedAt,
        IReadOnlyList<AdminAuditLogEntryDetailResponse> Entries);

    private sealed record RecoveryCodesResponse(
        DateTimeOffset GeneratedAt,
        IReadOnlyList<string> RecoveryCodes);

    private sealed record AdminIntegrationApiCredentialsResponse(
        IReadOnlyList<AdminIntegrationApiCredentialResponseModel> Credentials);

    private sealed record AdminIntegrationApiCredentialResponse(
        AdminIntegrationApiCredentialResponseModel Credential);

    private sealed record AdminIntegrationApiCredentialSecretResponse(
        AdminIntegrationApiCredentialResponseModel Credential,
        string Token);

    private sealed record AdminIntegrationApiCredentialResponseModel(
        Guid ProjectId,
        Guid Id,
        string Name,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastUsedAt,
        DateTimeOffset UpdatedAt,
        long Version);

    private sealed record AdminWebhookEndpointsResponse(
        IReadOnlyList<AdminWebhookEndpointResponseModel> Endpoints);

    private sealed record AdminWebhookEndpointResponse(
        AdminWebhookEndpointResponseModel Endpoint);

    private sealed record AdminWebhookEndpointResponseModel(
        Guid ProjectId,
        Guid Id,
        Guid IntegrationApiCredentialId,
        string Url,
        string SecretReference,
        string Status,
        IReadOnlyList<string> EventTypes,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        long Version);

    private sealed record AdminNativeEthAddressPoolSummaryResponse(
        int UnusedCount,
        int AssignedCount,
        int RetiredCount,
        int LowCapacityThreshold,
        bool IsLowCapacity);

    private sealed record AdminNativeEthAddressPoolImportResponse(
        Guid ImportId,
        int ImportedCount,
        AdminNativeEthAddressPoolSummaryResponse Summary);

    private sealed record AdminPaymentDetailResponse(
        Guid PaymentId,
        string ExternalReference,
        string FiatCurrency,
        long FiatAmountMinor,
        string Status,
        string PayerPageId,
        DateTimeOffset ExpiresAt,
        DateTimeOffset LateAcceptanceEndsAt,
        string? SelectedCurrency,
        string? ExpectedCryptoAmount,
        string? PaymentAddress,
        string? ObservedTotal,
        string? ConfirmedEligibleTotal,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? SettledAt,
        long Version);
}
