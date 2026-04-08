using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Moq;
using OtterApi.Enums;
using OtterApi.Middleware;
using OtterApi.Models;
using Xunit;

namespace OtterApi.Tests.Middleware;

/// <summary>
/// Contract: OtterApiMiddleware.IsAuthorizedAsync enforces the layered
/// authorization model: global Authorize flag → EntityPolicy → method-level policy.
/// </summary>
public class MiddlewareAuthorizationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OtterApiMiddleware CreateMiddleware()
        => new(ctx => Task.CompletedTask);

    private static ClaimsPrincipal Anonymous()
        => new(new ClaimsIdentity()); // IsAuthenticated == false

    private static ClaimsPrincipal Authenticated(string name = "user")
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "TestAuth"));

    private static OtterApiEntity EntityWith(
        bool authorize        = false,
        string? entityPolicy  = null,
        string? getPolicy     = null,
        string? postPolicy    = null,
        string? putPolicy     = null,
        string? deletePolicy  = null)
        => new()
        {
            Authorize     = authorize,
            EntityPolicy  = entityPolicy!,
            GetPolicy     = getPolicy!,
            PostPolicy    = postPolicy!,
            PutPolicy     = putPolicy!,
            DeletePolicy  = deletePolicy!,
        };

    private static IAuthorizationService AuthService(string? policyName = null, bool succeeds = true)
    {
        var mock = new Mock<IAuthorizationService>();
        var outcome = succeeds
            ? AuthorizationResult.Success()
            : AuthorizationResult.Failed();

        if (policyName == null)
        {
            // Any policy call → configured outcome
            mock.Setup(s => s.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<string>()))
                .ReturnsAsync(outcome);
        }
        else
        {
            mock.Setup(s => s.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    policyName))
                .ReturnsAsync(outcome);

            // All other policies succeed by default
            mock.Setup(s => s.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsNotIn(policyName)))
                .ReturnsAsync(AuthorizationResult.Success());
        }

        return mock.Object;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Authorize flag
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IsAuthorized_ReturnsFalse_WhenAnonymousAndAuthorizeIsTrue()
    {
        var sut    = CreateMiddleware();
        var entity = EntityWith(authorize: true);
        var auth   = AuthService(); // doesn't matter – blocked before calling it

        var result = await sut.IsAuthorizedAsync(auth, Anonymous(), entity, "GET");

        Assert.False(result);
    }

    [Fact]
    public async Task IsAuthorized_ReturnsTrue_WhenAnonymousAndAuthorizeIsFalse()
    {
        var sut    = CreateMiddleware();
        var entity = EntityWith(authorize: false);
        var auth   = AuthService(succeeds: true);

        var result = await sut.IsAuthorizedAsync(auth, Anonymous(), entity, "GET");

        Assert.True(result);
    }

    [Fact]
    public async Task IsAuthorized_ReturnsTrue_WhenAuthenticatedAndAuthorizeIsTrue_NoPolicy()
    {
        var sut    = CreateMiddleware();
        var entity = EntityWith(authorize: true);
        var auth   = AuthService(succeeds: true);

        var result = await sut.IsAuthorizedAsync(auth, Authenticated(), entity, "GET");

        Assert.True(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EntityPolicy
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IsAuthorized_ReturnsFalse_WhenEntityPolicyFails()
    {
        var sut    = CreateMiddleware();
        var entity = EntityWith(entityPolicy: "AdminOnly");
        var auth   = AuthService("AdminOnly", succeeds: false);

        var result = await sut.IsAuthorizedAsync(auth, Authenticated(), entity, "GET");

        Assert.False(result);
    }

    [Fact]
    public async Task IsAuthorized_ReturnsTrue_WhenEntityPolicySucceeds()
    {
        var sut    = CreateMiddleware();
        var entity = EntityWith(entityPolicy: "AdminOnly");
        var auth   = AuthService("AdminOnly", succeeds: true);

        var result = await sut.IsAuthorizedAsync(auth, Authenticated(), entity, "GET");

        Assert.True(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Method-level policies
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("GET",    "ReadPolicy",  null,          null,          null)]
    [InlineData("POST",   null,          "WritePolicy", null,          null)]
    [InlineData("PUT",    null,          null,          "WritePolicy", null)]
    [InlineData("DELETE", null,          null,          null,          "AdminOnly")]
    public async Task IsAuthorized_ReturnsFalse_WhenMethodPolicyFails(
        string method, string? getPolicy, string? postPolicy, string? putPolicy, string? deletePolicy)
    {
        var sut    = CreateMiddleware();
        var policy = getPolicy ?? postPolicy ?? putPolicy ?? deletePolicy!;
        var entity = EntityWith(getPolicy: getPolicy, postPolicy: postPolicy,
                                putPolicy: putPolicy, deletePolicy: deletePolicy);
        var auth   = AuthService(policy, succeeds: false);

        var result = await sut.IsAuthorizedAsync(auth, Authenticated(), entity, method);

        Assert.False(result);
    }

    [Theory]
    [InlineData("GET",    "ReadPolicy",  null,          null,          null)]
    [InlineData("POST",   null,          "WritePolicy", null,          null)]
    [InlineData("PUT",    null,          null,          "WritePolicy", null)]
    [InlineData("DELETE", null,          null,          null,          "AdminOnly")]
    public async Task IsAuthorized_ReturnsTrue_WhenMethodPolicySucceeds(
        string method, string? getPolicy, string? postPolicy, string? putPolicy, string? deletePolicy)
    {
        var sut    = CreateMiddleware();
        var entity = EntityWith(getPolicy: getPolicy, postPolicy: postPolicy,
                                putPolicy: putPolicy, deletePolicy: deletePolicy);
        var auth   = AuthService(succeeds: true);

        var result = await sut.IsAuthorizedAsync(auth, Authenticated(), entity, method);

        Assert.True(result);
    }

    // ── EntityPolicy takes precedence over method policy ─────────────────────

    [Fact]
    public async Task IsAuthorized_ReturnsFalse_WhenEntityPolicyFails_RegardlessOfMethodPolicy()
    {
        var sut    = CreateMiddleware();
        var entity = EntityWith(entityPolicy: "AdminOnly", getPolicy: "ReadPolicy");

        var auth = new Mock<IAuthorizationService>();
        auth.Setup(s => s.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), "AdminOnly"))
            .ReturnsAsync(AuthorizationResult.Failed());
        auth.Setup(s => s.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), "ReadPolicy"))
            .ReturnsAsync(AuthorizationResult.Success());

        var result = await sut.IsAuthorizedAsync(auth.Object, Authenticated(), entity, "GET");

        Assert.False(result);
    }

    // ── No policies → always allowed (if authenticated / not requiring auth) ──

    [Fact]
    public async Task IsAuthorized_ReturnsTrue_WhenNoPoliciesConfigured_AndAuthenticated()
    {
        var sut    = CreateMiddleware();
        var entity = EntityWith(authorize: false);
        var auth   = new Mock<IAuthorizationService>().Object;

        var result = await sut.IsAuthorizedAsync(auth, Authenticated(), entity, "GET");

        Assert.True(result);
    }

    [Fact]
    public async Task IsAuthorized_ReturnsTrue_WhenNoPoliciesConfigured_AndAnonymous()
    {
        var sut    = CreateMiddleware();
        var entity = EntityWith(authorize: false);
        var auth   = new Mock<IAuthorizationService>().Object;

        var result = await sut.IsAuthorizedAsync(auth, Anonymous(), entity, "GET");

        Assert.True(result);
    }
}


