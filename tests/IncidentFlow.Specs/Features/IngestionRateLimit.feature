Feature: Ingestion rate limit per source
  A source is the sending system, service and environment, compared without case.
  Each source may send 1,000 signals per one-minute window to Ingestion (the default).
  Anything over that is refused with a Retry-After, before it is saved or published,
  and one noisy source never uses up another source's allowance.

  Background:
    Given Ingestion runs with the default rate limit

  Scenario: The default limit is 1,000 signals a minute
    Then each source may send 1000 signals a minute

  Scenario: A source within its limit is accepted
    When "checkout-gateway" sends 1000 signals for "payment-api" in "prod" within a minute
    Then 1000 signals are accepted
    And 0 signals are refused

  Scenario: A source over its limit is refused with Retry-After
    When "checkout-gateway" sends 1001 signals for "payment-api" in "prod" within a minute
    Then 1000 signals are accepted
    And 1 signal is refused
    And the refusal tells the sender to retry within 1 minute

  Scenario Outline: Another source is unaffected
    Given "checkout-gateway" has used all its signals for "payment-api" in "prod" this minute
    When "<system>" sends 1 signal for "<service>" in "<environment>"
    Then 1 signal is accepted

    Examples:
      | system           | service      | environment |
      | billing-service  | payment-api  | prod        |
      | checkout-gateway | checkout-api | prod        |
      | checkout-gateway | payment-api  | staging     |

  Scenario: The same source in different case shares the allowance
    Given "checkout-gateway" has used all its signals for "payment-api" in "prod" this minute
    When "CHECKOUT-GATEWAY" sends 1 signal for "PAYMENT-API" in "PROD"
    Then 1 signal is refused
