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

When the app gets a feature that needs a newer relay (like cover prices, new releases or story arcs), copy the new code in. The desktop app tells you when it needs this. Your Metron login stays as it is.

1. Open [worker.js](worker.js), tap **Raw**, then select all and copy.
2. Go to **dash.cloudflare.com**, open **Workers & Pages**, then **comic-relay**.
3. Tap **Edit code**. Delete everything in the editor and paste in the new code.
4. Tap **Deploy**.

## Sending scans to your computer

The phone app can send new comics to the Comic Catalog app on your computer. They wait in a small free storage box (Cloudflare KV) on your relay until the computer collects them. Set it up once:

1. Update the relay code first (see *Updating the relay* above).
2. In Cloudflare, open **Storage & Databases**, then **KV** (it may be called *Workers KV*), and click **Create**. Name it `comic-inbox` and click **Add** or **Create**.
3. Open **Workers & Pages**, then **comic-relay**, then **Settings**, then **Bindings**. Click **Add**, choose **KV namespace**, and fill in:

   | Field | Value |
   | --- | --- |
   | Variable name | `INBOX` |
   | KV namespace | `comic-inbox` |

4. Click **Save** or **Deploy**.
5. On your phone: **Settings**, then **Send scans to your computer**, then **Turn on**. It shows a code.
6. On your computer: in Comic Catalog, open **Settings** and type that code under **Scans from your phone**, then click **Save**.

Only someone with the code can read what's in the box, and anything not collected is cleared after 30 days.

The same box also holds a copy of your collection and wishlist from the computer (relay version 6), so the phone can tell you in a shop whether you already own a comic. The computer replaces it whenever something changes.

Relay version 8 also sends who wrote and drew each comic, which the desktop app's Stats page uses.

Relay version 12 says how many issues each series has on Metron, so Missing issues (on the phone and the computer) can count to the end of a series, not just to the last issue you own.

Relay version 13 finds second printings and variant covers by their barcode. Metron usually lists only the first printing's barcode, so the relay looks that up instead and names the copy you scanned, e.g. "2nd printing". A barcode Metron doesn't know yet is asked about again the next day rather than a week later.

## Values from eBay (optional)

Relay version 9 can estimate what each comic is worth from what sellers are asking on eBay right now. It's free, but it needs your own eBay developer keys. Set it up once:

1. Sign up at **developer.ebay.com** (your normal eBay login works) and wait for the email saying your account is approved.
2. Sign in, open your name at the top right, then **Application Keysets**.
3. Under **Production**, give the keyset a name (for example `comic-relay`) and click **Create a keyset**.
4. eBay asks about **marketplace account deletion** notifications. Choose **Not persisting eBay data** (the relay doesn't keep anyone's eBay details), confirm it, and click **Submit**. The Production keys switch on after this.
5. Copy the **App ID (Client ID)** and the **Cert ID (Client Secret)**.
6. In Cloudflare, open **Workers & Pages**, then **comic-relay**, then **Settings**, then **Variables and Secrets**. Add two of type **Secret**:

   | Name | Value |
   | --- | --- |
   | `EBAY_CLIENT_ID` | the App ID |
   | `EBAY_CLIENT_SECRET` | the Cert ID |

7. Click **Deploy**.

Opening your relay's address with `/ping` on the end now shows `"ebay":true`.

Relay version 10 also finds wishlist comics listed at or under the most you'd pay, for the desktop app's *eBay deals*.

Relay version 11 keeps the page you're up to in each digital comic, so the phone's Read tab and the computer's Digital page carry on from the same place. It uses the same mailbox, with one small write each time you close a comic.

The estimate is the middle "Buy It Now" price of US listings for the same issue, in US dollars. Lots, sets, reprints and facsimiles are skipped, and a slabbed comic is only compared with slabs of the same grade. Asking prices usually run a bit above what comics actually sell for, so treat it as a guide. eBay allows 5,000 searches a day, and the relay remembers each answer for 3 days.

## Limits

Metron allows about 20 lookups a minute. The relay remembers each answer for a week, so scanning the same comic again doesn't count. Bulk lookups in the app pause between comics to stay under the limit.

Cloudflare's free plan allows 100,000 relay requests a day and, for the mailbox, 1,000 "list" and 1,000 "write" operations a day. The computer checks the mailbox every couple of minutes, so the relay remembers when it's empty instead of listing it each time, and the computer sends its collection to the phone at most every 30 minutes. If Cloudflare emails a usage warning, check that you have the latest relay code (`/ping` shows version 11 or later) and the latest Comic Catalog.
