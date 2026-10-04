# Comic lookup relay

The app finds comic details (title, cover and publisher) on [Metron](https://metron.cloud), a free community comic database. Web pages aren't allowed to call Metron directly, so you run a tiny free "relay" on Cloudflare. It passes the app's lookups to Metron, and it keeps your Metron password out of the app.

This takes about 10 minutes, and you only do it once.

## 1. Make a Metron account

1. Go to **metron.cloud** and tap **Sign up**.
2. Choose a username and password. You'll need both in step 3.

## 2. Make the relay on Cloudflare

1. Go to **dash.cloudflare.com/sign-up** and create a free account.
2. In the menu, open **Workers & Pages** (under *Compute*), then tap **Create**, then **Create Worker** (the "Hello World" one).
3. Name it `comic-relay` and tap **Deploy**.
4. Tap **Edit code**. Delete everything in the editor and paste in the whole of [worker.js](worker.js). (On that page, tap **Raw**, then select all and copy.)
5. Tap **Deploy**.

## 3. Add your Metron login to the relay

1. Go back to the worker's page and open **Settings**, then **Variables and Secrets**.
2. Add these three. Choose type **Secret** for the first two:

   | Name | Value |
   | --- | --- |
   | `METRON_USER` | your Metron username |
   | `METRON_PASS` | your Metron password |
   | `ALLOWED_ORIGIN` | `https://viksr-dev.github.io` |

3. Tap **Deploy**.

## 4. Connect the app

1. On the worker's page, copy its address. It looks like `https://comic-relay.yourname.workers.dev`.
2. In the Comic Collection app, open **Settings**, paste the address under **Comic lookup**, and tap **Save and test**.

When it says *Connected to Metron*, scans will fill in comic details automatically. Comics you scanned before this show up as **Needs details**. Tap **Look up details** at the top of your collection to fill them all in.

## Updating the relay

When the app gets a feature that needs a newer relay (like cover prices), copy the new code in. Your Metron login stays as it is.

1. Open [worker.js](worker.js), tap **Raw**, then select all and copy.
2. Go to **dash.cloudflare.com**, open **Workers & Pages**, then **comic-relay**.
3. Tap **Edit code**. Delete everything in the editor and paste in the new code.
4. Tap **Deploy**.

## Limits

Metron allows about 20 lookups a minute. The relay remembers each answer for a week, so scanning the same comic again doesn't count. Bulk lookups in the app pause between comics to stay under the limit.
