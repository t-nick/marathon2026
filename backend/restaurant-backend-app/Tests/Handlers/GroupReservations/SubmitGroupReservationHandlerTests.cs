using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Models.GroupReservation;
using APIHandler.Services.GroupReservations;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers.GroupReservations;

public class SubmitGroupReservationHandlerTests
{
    private readonly Mock<IGroupReservationService> _groupReservationService = new();
    private readonly Mock<ILambdaContext> _lambdaContext = new();
    private readonly Mock<ILambdaLogger> _logger = new();

    public SubmitGroupReservationHandlerTests()
    {
        _lambdaContext.Setup(context => context.Logger).Returns(_logger.Object);
    }

    [Fact]
    public async Task HandleRequest_AuthenticatedOwner_ReturnsExactAggregatedDishArray()
    {
        List<SubmittedDishQuantityResponse> aggregatedDishes =
        [
            new("dish-1", 5),
            new("dish-2", 2)
        ];
        _groupReservationService
            .Setup(service => service.SubmitGroupReservationAsync("owner-user", "group-1", "reservation-1"))
            .ReturnsAsync(aggregatedDishes);
        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("owner-user"), _lambdaContext.Object);

        Assert.Equal(200, result.StatusCode);
        using JsonDocument body = JsonDocument.Parse(result.Body);
        JsonElement[] entries = body.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(("dish-1", 5), (entries[0].GetProperty("dishId").GetString(), entries[0].GetProperty("quantity").GetInt32()));
        Assert.Equal(("dish-2", 2), (entries[1].GetProperty("dishId").GetString(), entries[1].GetProperty("quantity").GetInt32()));
        Assert.All(entries, entry => Assert.Equal(2, entry.EnumerateObject().Count()));
        _groupReservationService.Verify(
            service => service.SubmitGroupReservationAsync("owner-user", "group-1", "reservation-1"), Times.Once);
    }

    [Fact]
    public async Task HandleRequest_MissingAuthenticationClaim_ReturnsUnauthorizedWithoutCallingService()
    {
        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest(userId: null), _lambdaContext.Object);

        Assert.Equal(401, result.StatusCode);
        _groupReservationService.Verify(
            service => service.SubmitGroupReservationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleRequest_MissingGroupReservationPathParameter_ReturnsBadRequestWithoutCallingService()
    {
        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("owner-user", groupReservationId: null), _lambdaContext.Object);

        Assert.Equal(400, result.StatusCode);
        _groupReservationService.Verify(
            service => service.SubmitGroupReservationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("""{"reservationId":null}""")]
    [InlineData("""{"reservationId":""}""")]
    public async Task HandleRequest_MissingOrEmptyReservationId_ReturnsBadRequestWithoutCallingService(
        string? body)
    {
        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);
        APIGatewayProxyRequest request = CreateRequest("owner-user");
        request.Body = body;

        APIGatewayProxyResponse result = await handler.HandleRequest(request, _lambdaContext.Object);

        Assert.Equal(400, result.StatusCode);
        _groupReservationService.Verify(
            service => service.SubmitGroupReservationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"reservationId\":")]
    public async Task HandleRequest_MalformedJson_ReturnsBadRequestWithoutCallingService(string body)
    {
        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);
        APIGatewayProxyRequest request = CreateRequest("owner-user");
        request.Body = body;

        APIGatewayProxyResponse result = await handler.HandleRequest(request, _lambdaContext.Object);

        Assert.Equal(400, result.StatusCode);
        _groupReservationService.Verify(
            service => service.SubmitGroupReservationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleRequest_NonOwnerDeniedByService_ReturnsForbidden()
    {
        _groupReservationService
            .Setup(service => service.SubmitGroupReservationAsync("non-owner", "group-1", "reservation-1"))
            .ThrowsAsync(new ActionForbiddenException("Only the reservation owner can submit this group reservation."));
        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("non-owner"), _lambdaContext.Object);

        Assert.Equal(403, result.StatusCode);
    }

    [Theory]
    [InlineData("group")]
    [InlineData("reservation")]
    public async Task HandleRequest_MissingGroupOrReservation_ReturnsNotFound(string missingResource)
    {
        Exception exception = missingResource == "group"
            ? new GroupReservationNotFoundException()
            : new ReservationNotFoundException("Reservation was not found.");
        _groupReservationService
            .Setup(service => service.SubmitGroupReservationAsync("owner-user", "group-1", "reservation-1"))
            .ThrowsAsync(exception);
        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("owner-user"), _lambdaContext.Object);

        Assert.Equal(404, result.StatusCode);
    }

    [Theory]
    [InlineData("There are no submitted guest sessions.")]
    [InlineData("Submitted guest sessions contain no dishes.")]
    [InlineData("Order was not found for the reservation.")]
    public async Task HandleRequest_SubmitValidationFails_ReturnsBadRequest(string errorMessage)
    {
        _groupReservationService
            .Setup(service => service.SubmitGroupReservationAsync("owner-user", "group-1", "reservation-1"))
            .ThrowsAsync(new ValidationException(errorMessage));
        var handler = new SubmitGroupReservationHandler(_groupReservationService.Object);

        APIGatewayProxyResponse result = await handler.HandleRequest(
            CreateRequest("owner-user"), _lambdaContext.Object);

        Assert.Equal(400, result.StatusCode);
    }

    private static APIGatewayProxyRequest CreateRequest(
        string? userId,
        string? groupReservationId = "group-1")
    {
        var claims = new Dictionary<string, string>();
        if (userId is not null)
        {
            claims["sub"] = userId;
        }

        var pathParameters = new Dictionary<string, string>();
        if (groupReservationId is not null)
        {
            pathParameters["groupReservationId"] = groupReservationId;
        }

        return new APIGatewayProxyRequest
        {
            PathParameters = pathParameters,
            Body = """{"reservationId":"reservation-1"}""",
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayCustomAuthorizerContext { Claims = claims }
            }
        };
    }
}
