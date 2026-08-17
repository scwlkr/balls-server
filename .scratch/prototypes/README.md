# v0.3 endpoint-switching prototype evidence

This folder preserves the endpoint prototype evidence copied from
`prototype/v0.3-endpoint-switching` at
`012a424ba2d8ce23ada4f2b527a2404bbe28d5c0`; it is not product code. The
milestone copy adds only CRLF-tolerant extraction and the final specification's
endpoint-update binding/refusal and separate provider-target validation.

Run the durable verifier without opening a browser:

```powershell
node .scratch/prototypes/verify-v0.3-endpoint-switching-logic-prototype.js
```

It extracts and executes the inline pure `EndpointMachine` and seven guided
walkthroughs from the self-contained HTML file. It also checks the one-attempt,
no-automatic-fallback, credential-collision, IP-diagnostic, switch-preview,
endpoint-update host/grant/revision binding and refusal, real canonical UTC
generation time, endpoint-specific canonical provider-target proof, and 13
explicitly marked owner-interactive controls. It accepts LF or CRLF checkouts
and has no network, browser, persistence, credential, or drive-mapping access.
