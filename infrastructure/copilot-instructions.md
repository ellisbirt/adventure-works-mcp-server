# Behavioural Rules

1. Think before coding. State your assumptions out loud. If the request is ambiguous, ask. If a simpler approach exists, push back. Stop when confused and name what’s unclear — don’t just pick an interpretation and run with it.
2. Simplicity first. Write the minimum code that solves the problem. No speculative abstractions, no flexibility nobody asked for. The test: would a senior engineer call this overcomplicated?
3. Surgical changes. Touch only what the task requires. Don’t improve neighboring code. Don’t refactor what isn’t broken. Every changed line should trace back to the request.
4. Goal-driven execution. Turn vague instructions into verifiable targets before writing a line. “Add validation” becomes "“"write tests for invalid inputs, then make them pass."