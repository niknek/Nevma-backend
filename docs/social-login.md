# Social sign in

Nevma supports external sign in through Google and Facebook. Provider secrets must stay in environment variables or .NET user secrets and must never be committed.

## Local callback URLs

Register these exact redirect URLs in each provider console:

- Google: `http://localhost:5033/signin-google`
- Facebook: `http://localhost:5033/signin-facebook`

For production, register the same paths on the public HTTPS gateway origin, for example `https://api.example.com/signin-google` and `https://api.example.com/signin-facebook`. Use separate provider applications/projects for development and production. The production frontend origin must be listed under `Authentication:AllowedExternalReturnUrls` in the Identity service.

## Credentials

For local development, configure these .NET user-secret keys on the Identity project:

- `Authentication__Google__ClientId`
- `Authentication__Google__ClientSecret`
- `Authentication__Facebook__AppId`
- `Authentication__Facebook__AppSecret`

Restart Identity and Gateway after changing credentials. The endpoint `GET /identity/account/external/providers` reports whether each login option is ready without exposing secrets.

For `compose.backend.yaml`, configure `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `FACEBOOK_APP_ID`, `FACEBOOK_APP_SECRET`, and the HTTPS `NEVMA_APP_ORIGIN`. Production startup fails closed if either provider or an HTTPS return URL is missing. Keep all secrets in the deployment secret store, not in `.env` files committed to source control.

The provider callback never exposes a raw Nevma user ID. It returns a two-minute, server-protected login token that the client exchanges through `POST /identity/account/external/exchange`.

Google and Meta require the OAuth applications, redirect URIs, consent screen, verified domain, and production approval to be configured in their consoles. Those account-owned resources cannot be generated from this repository.

## Instagram

Instagram's API is not a general-purpose authentication provider. Nevma therefore does not simulate an Instagram login. Instagram users should choose Facebook / Meta sign in; a future Instagram account-linking feature can be added separately after the user is authenticated.
