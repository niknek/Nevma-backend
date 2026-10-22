# Social sign in

Nevma supports external sign in through Google and Facebook. Provider secrets must stay in environment variables or .NET user secrets and must never be committed.

## Local callback URLs

Register these exact redirect URLs in each provider console:

- Google: `http://localhost:5033/signin-google`
- Facebook: `http://localhost:5033/signin-facebook`

For production, register the same paths on the public gateway origin. The frontend return origins and the native deep link must also be listed under `Authentication:AllowedExternalReturnUrls` in the Identity service.

## Credentials

Configure:

- `Authentication__Google__ClientId`
- `Authentication__Google__ClientSecret`
- `Authentication__Facebook__AppId`
- `Authentication__Facebook__AppSecret`

Restart Identity and Gateway after changing credentials. The endpoint `GET /identity/account/external/providers` reports whether each login option is ready without exposing secrets.

## Instagram

Instagram's API is not a general-purpose authentication provider. Nevma therefore does not simulate an Instagram login. Instagram users should choose Facebook / Meta sign in; a future Instagram account-linking feature can be added separately after the user is authenticated.
