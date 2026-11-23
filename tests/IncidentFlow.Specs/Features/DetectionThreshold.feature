@redis
Feature: Error spike detection
  Detection raises an anomaly when the same error (service, environment and fingerprint)
  repeats five times within five minutes, measured by the signals' own timestamps.
  The window is a Redis sorted set; it is cleared when it fires, so the next anomaly
  needs five fresh errors. These scenarios run the real detector against a real Redis.

  Background:
    Given the detection window for "payment-api" in "prod" with fingerprint "payment-timeout" is empty

  Scenario: Five repeats within five minutes raise one anomaly
    When errors arrive at these minutes after the first one:
      | minute |
      | 0      |
      | 1      |
      | 2      |
      | 3      |
      | 4      |
    Then exactly 1 anomaly is raised
    And the anomaly is raised by the error at minute 4

  Scenario: Four repeats do not raise an anomaly
    When errors arrive at these minutes after the first one:
      | minute |
      | 0      |
      | 1      |
      | 2      |
      | 3      |
    Then no anomaly is raised
    And the window holds 4 errors

  Scenario: Repeats spread over more than five minutes do not raise an anomaly
    When errors arrive at these minutes after the first one:
      | minute |
      | 0      |
      | 1.5    |
      | 3      |
      | 4.5    |
      | 6      |
    Then no anomaly is raised
    And the window holds 4 errors

  Scenario: The window resets after an anomaly
    When errors arrive at these minutes after the first one:
      | minute |
      | 0      |
      | 0.5    |
      | 1      |
      | 1.5    |
      | 2      |
      | 2.5    |
    Then exactly 1 anomaly is raised
    And the window holds 1 error

  Scenario: A different fingerprint keeps its own window
    Given 4 errors with fingerprint "payment-timeout" arrived in the last minute
    When 1 error with fingerprint "card-declined" arrives
    Then no anomaly is raised
    And the window for "card-declined" holds 1 error
