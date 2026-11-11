# The audit page

Every election has a public audit page at
`vote.chisballot.org/audit/{slug}`. It is where a voter confirms their own
ballot was counted and where anyone, with no special tools, can recount the
vote and check that the site matches this repository.

**Status:** the application is not written yet. This page is the design the
audit page must follow. `docs/anonymity-and-security.md` lists what an
auditor should check in the code.

## What it shows

**While the election is Open** (read only, the same facts turnout already
makes public):

- Codes issued, codes used, ballots counted. Used must equal counted.
- Election settings: seats per contest, write-ins allowed, random order,
  live results on or off.
- Nothing else. No ballots, no receipt list and no per-choice counts, even
  if live results are on. Those stay on the election page, which is subject
  to the live-results setting.

**Once the election is Closed:**

1. **Audit numbers.** Codes issued, codes used, unused codes deleted,
   ballots counted, and a plain pass or fail on "used equals counted".
2. **Result per contest.** Winners, or **Tie: revote needed** with the tied
   choices and the seats still open.
3. **The receipt list.** Every receipt code, with a box to find yours. The
   search runs in the browser. The code you type is never sent to the server.
4. **Every anonymous ballot.** The choices on each ballot, including
   write-ins. Download as one JSON file and one CSV file.
5. **Recount in your browser.** A button that adds up the downloaded ballots
   with the same counting rules (including write-in normalization and the
   tie check) and compares the totals to the published result. A mismatch is
   shown in red.
6. **What is running.** The commit the site was built from, with a link to
   the repository at that commit and to the workflow run that deployed it.

## Rules

- The ballot list is published in receipt-code order. Receipt codes are
  random, so that order says nothing about when anyone voted. It is never
  storage order and never sorted by anything that could follow voting order.
- A ballot has no time and no sequence number, and neither does its row on
  this page or in the downloads.
- Election-level times (opened, closed) are fine to show. They describe the
  election, not a ballot.
- Nothing on the page is generated per visitor. Everyone sees the same
  files, so the page and downloads can be cached and mirrored.
- The audit page reads only published data and has no sign-in. Its database
  access is read only on closed elections, plus the three counts above while
  Open.
- Demo elections have an audit page too (`/audit/demo`) and work the same
  way.

## Why this matters

The audit page is the "anyone can check" promise made concrete. A voter can
check their ballot, a teacher can recount without trusting the site, and
someone technical can compare the running build to the code on GitHub. Where
that is not enough is listed in `docs/anonymity-and-security.md`, section
"What this does not protect against".
