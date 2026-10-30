using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Models.GuestSessions;
using APIHandler.Services.GuestSessions;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers.GuestSessions;

public class GetGroupSessionsHandlerTests
{
    private readonly Mock<IGuestSessionService> _guestSessionService = new();
    private readonly Mock<ILambdaContext> _lambdaContext = new();
    private readonly Mock<ILambdaLogger> _logger = new();

    public GetGroupSessionsHandlerTests()
    {
        _lambdaContext.Setup(context => context.Logger).Returns(_logger.Object);
    }

    [Fact]
    public async Task HandleRequest_AuthenticatedOwner_ReturnsSessionsResponse()
    {
        var response = new GetAllSessionsResponse(
            [
                new Participant(
                    "owner-session",
                    ParticipantType.Owner,
                    "Alex",
                    [
                        new ParticipantItem(
                            new ParticipantDish("dish-1", "Soup", 12.50m),
                            2)
                    ],
                    25m,
                    false)
            ],
            25m);
        _guestSessionService
            .Setup(s => s.GetGroupSessionsAsync("owner-user", "group-1"))
            .ReturnsAsync(response);

        var handler = new GetGroupSessionsHandler(_guestSessionService.Object);
        var result = await handler.HandleRequest(CreateRequest("owner-user"), _lambdaContext.Object);

        Assert.Equal(200, result.StatusCode);
        using var body = JsonDocument.Parse(result.Body);
        Assert.Equal(25m, body.RootElement.GetProperty("total").GetDecimal());
        var participant = Assert.Single(body.RootElement.GetProperty("participants").EnumerateArray());
        Assert.Equal("Owner", participant.GetProperty("type").GetString());
        Assert.Equal("Alex", participant.GetProperty("name").GetString());
        var dish = participant.GetProperty("items")[0].GetProperty("dish");
        Assert.Equal("dish-1", dish.GetProperty("id").GetString());
        Assert.Equal("Soup", participant.GetProperty("items")[0].GetProperty("dish").GetProperty("name").GetString());
        Assert.Equal(12.50m, dish.GetProperty("price").GetDecimal());
        Assert.Equal(3, dish.EnumerateObject().Count());
        Assert.Equal(2, participant.GetProperty("items")[0].GetProperty("quantity").GetInt32());
        _guestSessionService.Verify(s => s.GetGroupSessionsAsync("owner-user", "group-1"), Times.Once);
    }

    [Fact]
    public async Task HandleRequest_MissingSubClaim_ReturnsUnauthorizedWithoutCallingService()
    {
        var handler = new GetGroupSessionsHandler(_guestSessionService.Object);
        var result = await handler.HandleRequest(CreateRequest(userId: null), _lambdaContext.Object);

        Assert.Equal(401, result.StatusCode);
        _guestSessionService.Verify(
            s => s.GetGroupSessionsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleRequest_ServiceForbidsNonOwner_ReturnsForbidden()
    {
        _guestSessionService
            .Setup(s => s.GetGroupSessionsAsync("non-owner", "group-1"))
            .ThrowsAsync(new ActionForbiddenException("Only the reservation owner can perform this action."));
        var handler = new GetGroupSessionsHandler(_guestSessionService.Object);

        var result = await handler.HandleRequest(CreateRequest("non-owner"), _lambdaContext.Object);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task HandleRequest_GroupReservationNotFound_ReturnsNotFound()
    {
        _guestSessionService
            .Setup(s => s.GetGroupSessionsAsync("owner-user", "group-1"))
            .ThrowsAsync(new GroupReservationNotFoundException());
        var handler = new GetGroupSessionsHandler(_guestSessionService.Object);

        var result = await handler.HandleRequest(CreateRequest("owner-user"), _lambdaContext.Object);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task HandleRequest_ParentReservationNotFound_ReturnsNotFound()
    {
        _guestSessionService
            .Setup(s => s.GetGroupSessionsAsync("owner-user", "group-1"))
            .ThrowsAsync(new ReservationNotFoundException("Parent reservation not found."));
        var handler = new GetGroupSessionsHandler(_guestSessionService.Object);

        var result = await handler.HandleRequest(CreateRequest("owner-user"), _lambdaContext.Object);

        Assert.Equal(404, result.StatusCode);
    }

    private static APIGatewayProxyRequest CreateRequest(string? userId)
    {
        var claims = new Dictionary<string, string>();
        if (userId is not null)
        {
            claims["sub"] = userId;
        }

        return new APIGatewayProxyRequest
        {
            PathParameters = new Dictionary<string, string> { ["groupReservationId"] = "group-1" },
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayCustomAuthorizerContext { Claims = claims }
            }
        };
    }
}
