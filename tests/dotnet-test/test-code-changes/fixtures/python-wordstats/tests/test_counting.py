import unittest

from wordstats import average_word_length, count_words


class CountWordsTests(unittest.TestCase):
    def test_single_word(self):
        self.assertEqual(count_words("hello"), 1)

    def test_words_separated_by_one_space(self):
        self.assertEqual(count_words("the quick brown fox"), 4)

    def test_punctuation_stays_with_its_word(self):
        self.assertEqual(count_words("well, hello there!"), 3)


class AverageWordLengthTests(unittest.TestCase):
    def test_mean_length(self):
        self.assertEqual(average_word_length("ab abcd"), 3.0)

    def test_no_words(self):
        self.assertEqual(average_word_length(""), 0.0)


if __name__ == "__main__":
    unittest.main()
