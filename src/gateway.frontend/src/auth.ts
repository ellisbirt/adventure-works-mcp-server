import { PublicClientApplication } from '@azure/msal-browser'

const authority = import.meta.env.VITE_ENTRA_AUTHORITY
const clientId = import.meta.env.VITE_ENTRA_SPA_CLIENT_ID

export const authenticationEnabled = Boolean(authority && clientId && import.meta.env.VITE_ENTRA_API_SCOPE)
export const apiScope = import.meta.env.VITE_ENTRA_API_SCOPE ?? ''

export const msalInstance = new PublicClientApplication({
  auth: {
    clientId: clientId ?? '00000000-0000-0000-0000-000000000000',
    authority: authority ?? 'https://login.microsoftonline.com/common',
    redirectUri: window.location.origin,
  },
  cache: { cacheLocation: 'sessionStorage' },
})