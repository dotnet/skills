# README

This file contains a prompt, a bad output (without skill), and a good output (with skill).

The skill is considered successful if the output looks like the bad output without the skill, and like the good output with the skill. If the output looks like the good output without the skill, the skill is considered ineffective. If the output looks like the bad output with the skill and not like the good output with the skill, the skill is considered incorrect.

## Input prompt

My ASP.NET Core app is using 2GB of memory and growing. The container keeps getting OOM-killed every few hours. Can you help me figure out what's leaking?

