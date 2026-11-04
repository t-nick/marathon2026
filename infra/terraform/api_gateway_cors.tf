data "aws_api_gateway_rest_api" "restaurant_backend" {
  name = local.api_gateway_name
}

resource "aws_api_gateway_gateway_response" "cors" {
  for_each = toset([
    "ACCESS_DENIED",
    "DEFAULT_4XX",
    "DEFAULT_5XX",
    "UNAUTHORIZED",
  ])

  rest_api_id   = data.aws_api_gateway_rest_api.restaurant_backend.id
  response_type = each.value

  response_parameters = {
    "gatewayresponse.header.Access-Control-Allow-Headers" = "'Content-Type,Authorization,X-Amz-Date,X-Api-Key,X-Amz-Security-Token'"
    "gatewayresponse.header.Access-Control-Allow-Methods" = "'GET,POST,PUT,DELETE,OPTIONS,PATCH'"
    "gatewayresponse.header.Access-Control-Allow-Origin"  = "'*'"
  }
}
