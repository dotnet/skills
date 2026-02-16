# README

This file contains a prompt, a bad output (without skill), and a good output (with skill).

The skill is considered successful if the output looks like the bad output without the skill, and like the good output with the skill. If the output looks like the good output without the skill, the skill is considered ineffective. If the output looks like the bad output with the skill and not like the good output with the skill, the skill is considered incorrect.

## Input prompt

My API response times jumped from 50ms to 2 seconds after deploying yesterday. Can you help me profile what's happening?

