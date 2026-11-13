# Presenter notes for the board chair

Slide-by-slide notes for presenting `chisBallot-proposal.pptx` to the council
board and the teachers' union.

## Before the meeting

- **The one thing to get across:** this is a request for a side-by-side
  pilot. Paper stays the official result. Nobody is being asked to give
  anything up today.
- **The room:** principal, teachers, students, parents and union
  representatives. Use plain words. Say "code" and "slip", not "token" or
  "hash".
- **Length:** about 15 minutes of slides, then questions. Rough timing is
  given per slide.
- **Bring:** a few sample slips, a phone, and a stack of paper ballots. Being
  able to hold up a slip does more than any slide.
- **Run the live demo** after slide 3. The full script, including what to
  set up beforehand and what to say if it fails, is in `docs/demo.md`. The
  whole room can take part: everyone scans a QR code and votes on a practice
  question. Reset the demo and test it on your phone before people arrive.
  If the demo fails, say "and this is exactly when we switch to paper" and
  carry on.
- **Tone with the union:** agree early and often that paper works. The case
  is that this keeps what paper does well and adds a way for each voter to
  check the count.

---

## Slide 1: chisBallot (1 minute)

**Purpose:** say what this is and what you are asking for, in two sentences.

**Say:**
- "chisBallot is a small voting site built for this council and nothing
  else. It was built by one of our parents, and all of it is open for anyone
  to inspect."
- "I am not asking us to replace paper tonight. I am asking for a trial
  where we vote both ways once and compare."
- "I will show how it works, why a vote stays secret, why nobody can vote
  twice, and how any of us can check the count."

**Avoid:** technical detail here. Give the ask up front so nobody spends the
talk wondering where it is going.

---

## Slide 2: Paper stays in the room (2 minutes)

**Purpose:** lower the temperature. Most of the process does not change.

**Say:**
- "I still check the roster, exactly as I do now. One member, one turn."
- "Each member still gets a piece of paper. The difference is that the slip
  carries a one-time code instead of a list of names to circle."
- "Paper ballots are printed and sitting on the table. If the internet is
  down or anything looks wrong, we vote on paper on the spot. Anyone who
  has already voted on the site keeps that vote; everyone else swaps their
  slip for a paper ballot."
- Read the callout: the only change is where the ballot is marked and how it
  is counted.

**If asked "Then why bother?"** "Three reasons, and I will show each: every
voter can confirm their own ballot was counted, the count is immediate, and
no handwriting is involved."

---

## Slide 3: Voting day in four steps (2 minutes)

**Purpose:** let people picture themselves doing it.

**Say, walking through the four cards:**
1. "You come up and I mark you present on the roster."
2. "You pick any folded slip from the pile. I do not hand you one, you
   choose it. That matters for secrecy, and I will come back to it."
3. "You scan the code on the slip with your phone, or use the laptop on the
   table, and the ballot opens. You make your choices and submit."
4. "The screen shows a short receipt code. Write it down or take a picture.
   After the vote you can use it to confirm your ballot was counted."

- "It takes about a minute per person. With around forty of us it fits
  inside a normal meeting."

**If asked "What if I do not have a phone?"** "There will be a shared laptop
or tablet at the table, facing away from the room."

**If asked "What if I lose my slip before voting?"** "Tell me. I will cancel
the vote by paper rules for you, or we note one spoiled slip. An unused slip
is counted at the end like an unused paper ballot."

**Go straight to the demo slide.** The script is in `docs/demo.md`.

---

## Slide 4: Let's try it now (about 7 minutes)

**Purpose:** let everyone vote once on a practice question. This does more
than any other slide.

**Before the meeting:** on the admin site's Votes page press **Reset the demos**
(the demo opens by itself), load
the demo page on your phone early, do one test vote, then reset again. Keep
screenshots of each step in case the internet fails. The full script and the
reset steps are in `docs/demo.md`.

**Say:**
- "This is a practice vote. Nothing counts. Please scan the code."
- "At a real vote this would be the slip you draw from the pile. Today the
  button hands you one."
- "Look at your neighbor's phone. The questions and options are in a
  different order. Nobody can copy from anyone."
- When turnout climbs on the projector: "Turnout is always public. It shows
  how many have voted, never what."
- "I turned live results on for this practice. For a real vote I decide that
  first, and the default is off."
- After closing the demo on the admin site, open the audit page: "Find your
  receipt code. Your code never leaves your phone."
- Press **Recount in your browser**: "Anyone can add the ballots up
  themselves. This does it here, and it matches."
- Open the pre-filled tie demo: "If two people tie for the last seat, the
  site does not pick. It says revote needed." On the admin site, **Make the
  revote** starts that new vote in one step, with only the tied choices.

**If it fails:** "This is exactly when we switch to paper." Walk through the
screenshots and carry on.

---

## Slide 5: No one can tell who voted for whom (3 minutes)

**Purpose:** this is the most important slide for the union. Go slowly.

**Say:**
- "The site keeps two separate lists. One is the list of codes, and for each
  code only whether it has been used. The other is the list of ballots.
  Nothing connects the two."
- "The site never learns anyone's name. The roster stays on my clipboard and
  is never typed in."
- "Because you pick your own slip from a pile, nobody knows which code you
  hold. Not me, not the person who built the site."
- "A ballot is saved without a name, without the code and without the time.
  So even looking directly at the stored data, there is nothing to work
  back from."

**If asked "Could the person running it see how I voted?"** "No. What is
stored is a list of anonymous ballots. There is no name, code or time
attached to any of them, so there is nothing to look up."

**If asked "Could someone watch the order people vote in?"** "The ballots are
not stored in order and carry no time. Watching the room tells you no more
than watching people walk to a ballot box."

**Be honest about one limit:** "If a vote is unanimous, everyone knows how
everyone voted. That is true of paper too."

---

## Slide 6: One member, one vote (2 minutes)

**Purpose:** show that nobody can vote twice and nobody uninvited can vote.

**Say:**
- "Each code works once, and only for the vote it was printed for. Try it a
  second time and it is refused. Last month's slip does nothing this month."
- "Codes are long and random, and only the printed slips carry them. Someone
  guessing is blocked after a few wrong tries."
- Point to the line at the bottom: "After the vote we compare three numbers:
  how many people I signed off, how many slips were handed out, and how many
  ballots were counted. If ballots ever exceed slips, the vote is void and we
  redo it on paper."
- "Unused slips are collected and counted, the same way we would count
  unused paper ballots."

**If asked "Could someone photocopy a slip?"** "They could, but the copy
carries the same code, and it only works once. Whoever uses it first casts
the one vote that slip allows."

**If asked "Could someone print extra slips?"** "Only codes created for that
vote work, and the number created is fixed and published before voting
starts. An extra slip would have a code the site does not recognise."

**If asked "Who makes the slips, and could the admin look up a code?"** "The
board chair makes them once, on the admin site, by saying how many are needed.
The site shows the codes on one printable sheet, one time, and keeps only a
scrambled form of each. Nobody, including me, can look up a code or print a
second batch afterwards. If the printed sheet is lost, we make the vote again."

---

## Slide 7: Anyone can check the count (2 minutes)

**Purpose:** the one thing this does that a ballot box cannot.

**Say:**
- "With a ballot box, we trust the people counting. Here, every voter can
  check for themselves."
- "Every vote has a public audit page. After the vote closes it lists every
  receipt code. You look for yours. If it is there, your ballot was counted.
  The search runs on your phone, so your code is never sent to us."
- "The full list of anonymous ballots is on that page too, to download. Anyone
  who wants to can add them up by hand, or press the recount button and have
  the page do it."
- "The page also shows exactly which version of the code the site is
  running, with a link to it."
- "All of the code is public. A teacher, a parent or someone from district
  IT can read exactly what the site does."
- Point at the sample receipt: "It proves a ballot was counted. It does not
  show anyone else what the ballot says."

**If asked "Could the results be altered?"** "Someone with full control of
the server could try, but it would show. A removed ballot means a voter's
receipt is missing. An added ballot means more ballots than slips. Both
checks are in the open."

---

## Slide 8: Our data stays ours (1 minute)

**Purpose:** answer "who sees our data?" before anyone asks, without
overclaiming.

**Say:**
- "There is no voting company in the middle. No ballot vendor, no
  counting service, no account with anyone who sells voting software. The
  code is open and we run it ourselves."
- "There are no analytics, no ads and no trackers on the site."
- "It runs on a small server we run ourselves, not on a vendor's cloud. Two
  outside services are involved: Cloudflare, which handles the domain and
  carries the traffic to our server, and GitHub, which holds the code. What
  passes through them is anonymous ballots. Nothing says who cast which one."

**Do not say** that nobody can see the data, or that no third-party service
is involved. Both are false, and someone in the room will notice. Cloudflare
sits in front of the sites and sees the traffic in transit, and our own
backups are encrypted copies kept on our own equipment; those limits are
written down in `docs/anonymity-and-security.md`.

**If asked "Who can see our data?"** "The people who run the server can see
what is stored, which is anonymous ballots. Cloudflare carries the traffic
and can see it pass. Nobody can see who voted for what, because we never
store it."

---

## Slide 9: The chair sets up each vote (2 minutes)

**Purpose:** show the options and why the risky ones start switched off.

**Say:**
- "A question can fill more than one seat. Three board members out of ten
  candidates is one question. President and vice president are separate
  questions. Only who wins matters, not the order."
- "If two people tie for the last seat, the site does not pick for us, and
  neither do I. It marks the result revote needed. Clear winners keep their
  seats and only the tied choices go to a new vote."
- "Live results are off unless I turn them on before the vote. A total that
  moves right after a known member votes can point at that member. Turnout,
  how many have voted, is always public."
- "Questions and options can be shuffled for each voter so nobody can copy a
  neighbor and results do not arrive in a pattern."
- "A question can allow a write-in name. It is off unless I turn it on,
  because a typed word can be used to prove how you voted. Names show only
  after the vote closes."
- "All of these are fixed once the vote opens. I cannot change them
  halfway."

**If asked "Why would you ever turn live results on?"** "For a low-stakes
vote where seeing the count build is useful, like a show of hands. Not for
anything where one person's choice matters."

---

## Slide 10: How it compares with paper (2 minutes)

**Purpose:** be even-handed. Do not read every cell.

**Say:**
- "Paper works, and this table says so. On most rows the two are simply
  different ways to do the same job."
- Pick out three rows:
  - **Secrecy:** "No handwriting to recognise."
  - **Checking:** "Each voter checks their own receipt instead of trusting
    the counters."
  - **If it fails:** "We switch to paper on the spot. Paper is the safety
    net, which is why it stays."
- "The honest weakness is that this needs a working internet connection.
  That is the reason for the paper backup."

---

## Slide 11: Why not a form, a poll or a service? (2 minutes)

**Purpose:** answer "why not something we already have?" before it is asked.
Be fair: these tools are good at what they are for. They are not built for a
secret ballot.

**Say:**
- **Online forms (Google, Microsoft):** "To allow one answer per person, a form
  makes each voter sign in. Now an account is tied to an answer. Every
  response also carries a time and sits in the owner's account, where it can
  be read, edited or deleted. And a voter cannot check their own answer was
  counted."
- **Paid voting services:** "A company holds the roster and the ballots, the
  code is closed, and every election costs money. You are trusting their
  word."
- **Chat polls and poll sites:** "'Anonymous' is a setting the owner controls,
  and the tool can still record times or addresses. Without a login, nothing
  stops one person voting twice."
- **Raising hands:** "Everyone sees how everyone votes. That is exactly what
  a secret ballot is for."
- "chisBallot does the opposite on each point: no login, no time on a ballot,
  every voter can check their own, and the code is open."

**Do not say** that any of these products is insecure or careless. The point is
what they are designed to do, not what they get wrong.

---

## Slide 12: Small, simple and free to run (1 minute)

**Purpose:** remove cost and data worries.

**Say:**
- "It runs on a small server we already have. The only cost is the domain name.
  There is no budget request."
- "No names are stored. The roster never leaves this room."
- "It is sized for a board like ours, and it can be reused for every vote we
  hold. Each vote gets a fresh set of slips, and past results stay on
  record."

**If asked "Who is responsible for it?"** "The setup lives in a public
repository, and setting up a vote happens on a separate, sign-in-only admin
site. Only people we invite can create a vote or make its slips, and slips
can be made only once per vote."

**If asked "Does it use district accounts or student data?"** "Neither."

**If asked "What happens if the site fails halfway through a vote?"** "I
switch to paper right then. I close the vote on the site, so no more digital
ballots come in; the ones already cast stay counted. Each member who has not
voted hands their slip back to me and takes a paper ballot, one for one. We
count the paper ballots by hand with two witnesses, on a count sheet that
checks the numbers add up, and I announce both results. The paper count is
the official record. The steps are in `docs/paper-fallback.md`, and the paper
ballots and count sheet are printed from the vote's page on the admin site
before the meeting."

---

## Slide 13: The ask: a side-by-side pilot (1 minute, then questions)

**Purpose:** end on a small, safe decision.

**Say:**
1. "At our next vote, we vote twice: once on paper, once on chisBallot."
2. "Board members and union representatives compare the two counts and the
   audit numbers."
3. "Paper stays the official result until both groups are satisfied. If
   either group is not, we stop, and nothing has been lost."

- "What I am asking for tonight is agreement to run that one trial."

**Then stop talking and take questions.**

---

## Questions to be ready for

| Question | Short answer |
|---|---|
| What if the internet goes down mid-vote? | We stop and redo the whole vote on paper. Partial electronic results are discarded. |
| What if someone votes and then says the site got it wrong? | Their receipt lets them confirm a ballot was counted. If a member disputes the outcome, the board can order a paper re-vote. |
| Can a member vote from home? | No. Slips are only handed out in the room, after roster sign-off. |
| Is it accessible? | The ballot is a plain web page that works with phone screen readers and large text. Anyone who prefers can vote on paper. |
| Can slips be reprinted? | No. The site keeps only a scrambled form of each code, so a lost sheet means making the vote again. |
| Who can set up a vote? | Only invited admins, on a separate sign-in-only site. The public voting site has no admin functions. |
| Who has reviewed it? | The code is public so anyone can. Offer the union the chance to name a reviewer before the pilot. |
| What happens to old votes? | Results, receipt lists and audit numbers stay published. Unused codes are deleted when a vote closes. |
| What does it cost if we outgrow it? | Nothing changes at our size. It runs on a small server we already own, so the cost is a domain name and electricity. |
| Do you use any third-party services? | Yes: Cloudflare for the domain and for carrying traffic to our server, and GitHub for the code. The sites themselves run on a server we operate. There is no voting vendor, and no analytics or ad trackers. |
| What if there is a tie? | The site does not break it. The result says revote needed, clear winners keep their seats, and the tied choices go to a new vote with fresh slips. |
| Can the chair see how people voted? | No. Ballots hold no name, code or time. The chair sees turnout, and results only when the vote closes, or live if that was switched on beforehand. |
| Can someone type something silly as a write-in? | Write-ins are off unless the chair turns them on, are limited in length and characters, and show only after the vote closes. |
| How do I know the running site is the code on GitHub? | The audit page shows the commit the site was built from and links to the deploy record. That narrows the gap but is not a cryptographic proof. |
| Why not buy a commercial product? | Commercial tools start at a monthly fee for up to 50 voters and their code cannot be inspected. This is free and open. |

## If the discussion gets stuck

- Return to the ask: "This is one trial, with paper as the result that
  counts."
- Offer the union a concrete role: choosing a reviewer, observing the slip
  count, and co-signing the comparison afterwards.
- Do not promise a date for dropping paper. That is a later decision for the
  board.
