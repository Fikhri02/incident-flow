Feature: Incident lifecycle
  An incident moves open -> acknowledged -> resolved -> closed.
  It may also be resolved straight from open, and closed from any status except closed.
  The Incident aggregate refuses every other change, and records one domain event
  for each change it accepts (the service publishes those events through its outbox, in the same transaction as the change).

  Background:
    Given an incident opened for "payment-api" in "prod" with fingerprint "payment-timeout"

  Scenario: A new incident starts open
    Then the incident is "open"
    And the incident has recorded these events:
      | event             |
      | IncidentWasOpened |

  Scenario: An incident goes through the whole lifecycle
    When "dana" acknowledges the incident
    And "dana" resolves the incident
    And the incident is closed
    Then the incident is "closed"
    And it was acknowledged and resolved by "dana"
    And the incident has recorded these events:
      | event                   |
      | IncidentWasOpened       |
      | IncidentWasAcknowledged |
      | IncidentWasResolved     |
      | IncidentWasClosed       |

  Scenario: An open incident can be resolved without being acknowledged
    When "dana" resolves the incident
    Then the incident is "resolved"

  Scenario Outline: A change the current status does not allow is refused
    Given the incident is "<status>"
    When someone tries to <change> the incident
    Then the change is refused with "<reason>"
    And the incident is still "<status>"
    And no new event is recorded

    Examples:
      | status       | change      | reason                                               |
      | acknowledged | acknowledge | Cannot acknowledge incident in status 'acknowledged'. |
      | resolved     | acknowledge | Cannot acknowledge incident in status 'resolved'.     |
      | closed       | acknowledge | Cannot acknowledge incident in status 'closed'.       |
      | resolved     | resolve     | Incident is already 'resolved'.                       |
      | closed       | resolve     | Incident is already 'closed'.                         |
      | closed       | close       | Incident is already closed.                           |

  Scenario: Acknowledging needs a person's name
    When someone acknowledges the incident without giving a name
    Then the request is rejected as invalid
    And the incident is still "open"
