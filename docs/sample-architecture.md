# Example service architecture

## Context

The application has a public API gateway and two internal services: the document service and the audit service. The gateway authenticates requests with OAuth 2.0 access tokens. The document service stores files in encrypted object storage and publishes metadata events to a message queue. The audit service consumes those events and retains audit records for 90 days.

## Availability and recovery

The gateway and document service run in two availability zones. The queue is configured for at-least-once delivery. The current design document does not specify a recovery-time objective or a recovery-point objective.

## Identity and secrets

Production credentials are provisioned through the cloud secret manager. Example configuration uses `YOUR_API_KEY` as a placeholder and does not contain a production credential.
