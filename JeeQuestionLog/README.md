# JEE Question Log

A single-file web app for tracking the questions a student solves while preparing for JEE Advanced.

Open `index.html` in any browser. It needs no install and no server, and works on a phone.

## What it does

- **Log a question set**: subject (Physics, Chemistry, Maths), chapter (JEE Advanced syllabus suggestions, or type your own), source (PYQs, coaching modules, Cengage, HCV, Irodov, N. Avasthi and so on, or any name you type), date, question type (single correct, multiple correct, integer/numerical, paragraph, matrix match), attempted, correct, minutes and a note.
- **Summary**: today's count against a daily goal, practice streak and best run, total solved, overall accuracy, days left until the exam.
- **Last 30 days**: stacked bar chart per subject with the daily goal line.
- **By subject** and **accuracy by question type**, so you can see whether multiple-correct or matrix-match questions are costing marks.
- **Chapters**: solved, sessions, accuracy and last practised for each chapter, flagged *Needs work* (under 60% across 10+ questions) or *Rusty* (not practised for 14+ days).
- **Recent sessions** with delete.
- **Settings and backup**: your name (shown to your teacher), daily goal, exam date, export to JSON (backup) or CSV (spreadsheet), import a JSON backup.
- **Students** (teacher view): a class table with each student's questions today, last 7 days, total, accuracy, streak, last activity and weakest chapters. Click a name to open that student's full log, read-only.

## Where data is stored

Opened as a local file, entries are saved in that browser's `localStorage`. Export a JSON backup now and then, and import it to move the log to another device or browser.

## Seeing students' progress with the local file

1. Each student fills in *Your name* under Settings and taps *Export backup (.json)*.
2. They send you the file.
3. Open your copy of `index.html`, go to **Students**, and tap *Add a student's backup file*. Adding a newer file from the same student replaces the older one.
