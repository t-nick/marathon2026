using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Models.GuestSessions;
using APIHandler.Services.GuestSessions;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers.GuestSessions;

public class DeleteGuestSessionHandlerTests
{
    private readonly Mock<IGuestSessionService> _guestSessionService = new();
    private readonly Mock<ILambdaContext> _lambdaContext = new();
    private readonly Mock<ILambdaLogger> _logger = new();

    public DeleteGuestSessionHandlerTests()
    {
        _lambdaContext.Setup(context => context.Logger).Returns(_logger.Object);
    }

    [Fact]
    public async Task HandleRequest_AuthenticatedOwner_ReturnsUpdatedSnapshotWithCamelCaseJson()
    {
        var response = new GetAllSessionsResponse(
            [
                new Participant(
                    "remaining-session",
                    ParticipantType.Guest,
                    "Remaining guest",
                    [new ParticipantItem(new ParticipantDish("dish-1", "Soup", 7.50m), 2)],
                    15m,
                    false)
            ],
            15m);
        _guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync("owner-user", "group-1", "deleted-session"))
            .ReturnsAsync(response);

        var handler = new DeleteGuestSessionHandler(_guestSessionService.Object);
        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("owner-user"), _lambdaContext.Object);

        Assert.Equal(200, result.StatusCode);
        using var body = JsonDocument.Parse(result.Body);
        JsonElement root = body.RootElement;
        Assert.Equal(15m, root.GetProperty("total").GetDecimal());
        Assert.Equal(2, root.EnumerateObject().Count());
        JsonElement participant = Assert.Single(root.GetProperty("participants").EnumerateArray());
        Assert.Equal("remaining-session", participant.GetProperty("id").GetString());
        Assert.Equal(15m, participant.GetProperty("total").GetDecimal());
        Assert.Equal(6, participant.EnumerateObject().Count());
        JsonElement item = Assert.Single(participant.GetProperty("items").EnumerateArray());
        Assert.Equal("dish-1", item.GetProperty("dish").GetProperty("id").GetString());
        Assert.Equal(7.50m, item.GetProperty("dish").GetProperty("price").GetDecimal());
        _guestSessionService.Verify(
            service => service.DeleteGuestSessionAsync("owner-user", "group-1", "deleted-session"),
            Times.Once);
    }

    [Fact]
    public async Task HandleRequest_ServiceReportsMissingSession_ReturnsNotFound()
    {
        _guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync("owner-user", "group-1", "missing-session"))
            .ThrowsAsync(new GuestSessionNotFoundException(
                "Guest session with id missing-session not found."));
        var handler = new DeleteGuestSessionHandler(_guestSessionService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("owner-user", "missing-session"), _lambdaContext.Object);

        Assert.Equal(404, result.StatusCode);
        using var body = JsonDocument.Parse(result.Body);
        Assert.Equal(
            "Guest session with id missing-session not found.",
            body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task HandleRequest_ServiceReportsSessionInAnotherGroup_ReturnsBadRequest()
    {
        _guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync("owner-user", "group-1", "session-1"))
            .ThrowsAsync(new ValidationException(
                "Guest session does not belong to this group reservation."));
        var handler = new DeleteGuestSessionHandler(_guestSessionService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("owner-user", "session-1"), _lambdaContext.Object);

        Assert.Equal(400, result.StatusCode);
        using var body = JsonDocument.Parse(result.Body);
        Assert.Equal(
            "Guest session does not belong to this group reservation.",
            body.RootElement.GetProperty("message").GetString());
        Assert.True(body.RootElement.TryGetProperty("errorCode", out _));
        Assert.False(body.RootElement.TryGetProperty("Message", out _));
    }

    [Fact]
    public async Task HandleRequest_ServiceForbidsNonOwner_ReturnsForbidden()
    {
        _guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync("other-user", "group-1", "session-1"))
            .ThrowsAsync(new ActionForbiddenException(
                "Only the reservation owner can perform this action."));
        var handler = new DeleteGuestSessionHandler(_guestSessionService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("other-user", "session-1"), _lambdaContext.Object);

        Assert.Equal(403, result.StatusCode);
    }

    private static APIGatewayProxyRequest CreateRequest(
        string? userId,
        string guestSessionId = "deleted-session")
    {
        var claims = new Dictionary<string, string>();
        if (userId is not null)
        {
            claims["sub"] = userId;
        }

        return new APIGatewayProxyRequest
        {
            PathParameters = new Dictionary<string, string>
            {
                ["groupReservationId"] = "group-1",
                ["guestSessionId"] = guestSessionId
            },
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayCustomAuthorizerContext { Claims = claims }
            }
        };
    }
}
