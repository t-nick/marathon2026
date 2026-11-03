using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.Reservations;
using APIHandler.Services.GuestSessions;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers.Reservations;

public class GetOwnerGuestSessionIdHandlerTests
{
    private readonly Mock<IGuestSessionService> _guestSessionService = new();
    private readonly Mock<ILambdaContext> _lambdaContext = new();
    private readonly Mock<ILambdaLogger> _logger = new();

    public GetOwnerGuestSessionIdHandlerTests()
    {
        _lambdaContext.Setup(context => context.Logger).Returns(_logger.Object);
    }

    [Fact]
    public async Task HandleRequest_AuthenticatedOwner_ReturnsOwnerGuestSessionId()
    {
        _guestSessionService
            .Setup(service => service.GetOwnerGuestSessionIdAsync("owner-user", "reservation-1"))
            .ReturnsAsync("owner-session-1");
        var handler = new GetOwnerGuestSessionIdHandler(_guestSessionService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("owner-user"), _lambdaContext.Object);

        Assert.Equal(200, result.StatusCode);
        using JsonDocument body = JsonDocument.Parse(result.Body);
        Assert.Equal(1, body.RootElement.EnumerateObject().Count());
        Assert.Equal("owner-session-1", body.RootElement.GetProperty("guestSessionId").GetString());
        _guestSessionService.Verify(
            service => service.GetOwnerGuestSessionIdAsync("owner-user", "reservation-1"), Times.Once);
    }

    [Fact]
    public async Task HandleRequest_MissingAuthenticationClaim_ReturnsUnauthorizedWithoutCallingService()
    {
        var handler = new GetOwnerGuestSessionIdHandler(_guestSessionService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest(userId: null), _lambdaContext.Object);

        Assert.Equal(401, result.StatusCode);
        _guestSessionService.Verify(
            service => service.GetOwnerGuestSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleRequest_NonOwnerDeniedByService_ReturnsForbidden()
    {
        _guestSessionService
            .Setup(service => service.GetOwnerGuestSessionIdAsync("non-owner", "reservation-1"))
            .ThrowsAsync(new ActionForbiddenException("Only the reservation owner can access the owner guest session."));
        var handler = new GetOwnerGuestSessionIdHandler(_guestSessionService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("non-owner"), _lambdaContext.Object);

        Assert.Equal(403, result.StatusCode);
    }

    [Theory]
    [InlineData("reservation")]
    [InlineData("group")]
    [InlineData("session")]
    public async Task HandleRequest_MissingReservationOrOwnerSession_ReturnsNotFound(string missingResource)
    {
        Exception exception = missingResource switch
        {
            "reservation" => new ReservationNotFoundException("Reservation was not found."),
            "group" => new GroupReservationNotFoundException(),
            _ => new GuestSessionNotFoundException("Owner guest session was not found.")
        };
        _guestSessionService
            .Setup(service => service.GetOwnerGuestSessionIdAsync("owner-user", "reservation-1"))
            .ThrowsAsync(exception);
        var handler = new GetOwnerGuestSessionIdHandler(_guestSessionService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("owner-user"), _lambdaContext.Object);

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
            PathParameters = new Dictionary<string, string> { ["reservationId"] = "reservation-1" },
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayCustomAuthorizerContext { Claims = claims }
            }
        };
    }
}
