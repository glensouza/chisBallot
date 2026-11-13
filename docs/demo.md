# Demo mode and live demo script

A live demo during the meeting does more than any slide: people see a
ballot open on their own phone, vote, and check the receipt. Demo mode
exists for that, and it has to be safe, because the demo runs on the same
site as real votes.

**Status:** built. The home page has the **Try the demo** button while the demo is
open, and the demo-slip button is rate limited. `/e/demo-tie` shows the
result, and `/audit/demo-tie` the audit page with the recount. Nothing here changes how
a real election works.

## How demo mode works

- A demo is an ordinary election with `IsDemo` set. It runs the real code
  path (slip, ballot, receipt, results, audit page), so the audience watches
  the real thing, not a mock-up.
- `IsDemo` is set when an admin creates the election and never changes. A
  real election cannot become a demo and a demo cannot become real.
- While a demo is Open, `vote.chisballot.org` shows **Try the demo**. When
  no demo is Open, the button is hidden and the page only says a QR code is
  needed.
- The demo page (`/e/demo`) has a **Get a demo slip** button. It returns a
  fresh single-use code for the demo election only, then opens the ballot at
  `/v/{slug}#{code}`. In a real vote that slip comes from the pile on the table.
- Every demo page carries a fixed banner: **DEMO: not a real vote**, in a
  different color from real ballots.
- Demo elections turn on everything worth showing: several seats, a write-in
  box, random order and live results. Real elections default to all of them
  off.
- Demo data is fake and temporary. Both demos are made automatically on the
  first deploy. To run a fresh one, reset them on the server (`ops/README.md`,
  "The demo"); reset deletes and remakes only elections marked as demos.
- A second, pre-filled demo, `/e/demo-tie`, is already Closed and shows a
  tie ending in **Tie: revote needed**. It holds made-up ballots, so there is
  nothing to wait for.

## What keeps the demo from touching a real vote

| Measure | Where to check |
|---|---|
| Issuing a code from the public site works only through one database procedure. It refuses any election that is not a demo, is not Open, or already has the demo limit of codes. | The procedure in the migration; the `chisballot_vote` grants (execute on that procedure, no insert on `VotingCodes`) |
| Real elections have no public code-issuing endpoint at all. | Vote API routes |
| Demo and real data never mix: every count, audit number and list is per election. | Queries all filter on the election id |
| The demo-slip button is rate limited per client address (100 slips in 10 minutes), and the database stops at 300 slips per demo. | `VoteLimits` in `VoteEndpoints.cs`; `issue_demo_code` in `003_demo.sql` |
| Resetting demos is one database function that can reach only the two demos and any revote made from them; the admin role cannot delete ballots itself. | `ballot.reset_demos` in `006_demo_functions.sql`; `DemoTests` |
| A demo follows every anonymity rule: no name, code or time on a ballot, and nothing logged. | `docs/anonymity-and-security.md` |

## Demo script (about 7 minutes, after slide 3)

**Before the meeting (the day before, and again 15 minutes early)**

1. On the admin site, **Votes**, then **Reset the demos**. The demo opens by
   itself. Its questions are "Pick three field trip spots" (six options, three
   seats, write-in on) and "Should lunch be longer?" (yes or no).
2. Load `vote.chisballot.org/e/demo` on your phone to check it comes up.
3. Do one full test vote on your phone, then reset again.
4. Put the demo page on the projector. Keep screenshots of each step ready
   in case the internet fails.
5. Bring two sample paper slips to hold up while you talk.

**On the day**

| Time | You do | You say |
|---|---|---|
| 0:00 | Show `vote.chisballot.org` and click **Try the demo** | "This is the page anyone sees. Without a slip there is nothing to vote on. Today I have a practice vote. Nothing counts." |
| 0:30 | Show the QR code slide for `vote.chisballot.org/e/demo` and ask everyone to scan it | "At a real vote this would be the slip you draw from the pile. Today the button hands you one." |
| 1:30 | Walk the room, glance at two screens | "Look at your neighbor's phone. The questions and options are in a different order. Nobody can copy from anyone." |
| 2:30 | Let people vote. Point at the projector as turnout climbs | "Turnout is always public. It shows how many have voted, never what." |
| 3:30 | Point at the live results | "I turned live results on for this practice. For a real vote I decide that first, and the default is off, because a running total can hint at how the last person voted." |
| 4:30 | Ask one person to type a write-in | "A question can allow a write-in name. It is off unless I turn it on, and names show only after the vote closes." |
| 5:00 | Close the demo on the admin screen and open `vote.chisballot.org/audit/demo` | "Now it is closed. This is the audit page every election has. Slips issued, slips used, ballots counted. Used and counted match." |
| 6:00 | Ask everyone to type their receipt code into the search box on the audit page | "Find your code. Your ballot is in the list and nobody can tell which one is yours. Your code never leaves your phone." |
| 6:30 | Tap **Recount in your browser** | "Anyone can add up the ballots themselves. This does it here, and it matches." |
| 6:45 | Open `/e/demo-tie` | "If two people tie for the last seat, the site does not pick for us. It says revote needed, and the tied choices go to a new vote, which I start with one button on the admin site." |

**If something goes wrong**

- Slow first load: "The page downloads the voting app each time, on purpose,
  so nobody ever runs an old copy. On school wifi it takes a few seconds."
- No internet or a failure: "This is exactly when we switch to paper." Walk
  through the screenshots, then continue.
- A silly write-in or answer: say "that is why write-ins are off unless I
  turn them on," and reset afterwards.
- Someone asks if their demo vote was recorded: "Yes, as an anonymous
  practice ballot. The whole demo is deleted when I reset it."

**After the meeting:** reset the demo, or close it so the button disappears.
