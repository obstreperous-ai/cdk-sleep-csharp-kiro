"""Unit tests for the Sleep Audio Processor Lambda handler."""

import json
import os
import unittest
from unittest.mock import MagicMock, patch, ANY

import index


class TestHandler(unittest.TestCase):
    """Tests for the Lambda handler function."""

    def setUp(self):
        """Set up test fixtures."""
        self.env_vars = {
            "TABLE_NAME": "test-metadata-table",
            "INPUT_BUCKET_NAME": "test-input-bucket",
            "OUTPUT_BUCKET_NAME": "test-output-bucket",
        }
        self.valid_event = {
            "detail": {
                "bucket": {"name": "test-input-bucket"},
                "object": {"key": "test-file.mp3"},
            }
        }
        self.context = MagicMock()
        self.context.aws_request_id = "test-request-id-123"

    def _make_event(self, bucket_name="test-input-bucket", key="test-file.mp3"):
        """Create a valid event with the given bucket and key."""
        return {
            "detail": {
                "bucket": {"name": bucket_name},
                "object": {"key": key},
            }
        }

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    @patch("index.s3_client")
    @patch("index.dynamodb")
    def test_valid_mp3_processing(self, mock_dynamodb, mock_s3):
        """Test successful processing of a valid MP3 file."""
        # Mock S3 head_object for size check
        mock_s3.head_object.return_value = {"ContentLength": 1024}

        # Mock S3 get_object
        mock_s3.get_object.return_value = {
            "Body": MagicMock(read=lambda: b"fake-audio-content"),
            "ContentType": "audio/mpeg",
            "ContentLength": 1024,
        }

        # Mock DynamoDB table
        mock_table = MagicMock()
        mock_dynamodb.Table.return_value = mock_table

        event = self._make_event(key="test-file.mp3")
        result = index.handler(event, self.context)

        assert result["statusCode"] == 200
        assert result["body"]["audioId"] == "test-file.mp3"
        assert result["body"]["status"] == "PROCESSED"
        assert result["body"]["inputBucket"] == "test-input-bucket"
        assert result["body"]["outputBucket"] == "test-output-bucket"
        assert "outputKey" in result["body"]
        assert result["body"]["outputKey"].startswith("processed/")

        # Verify S3 put_object was called for upload
        mock_s3.put_object.assert_called_once()
        put_call = mock_s3.put_object.call_args
        assert put_call[1]["Bucket"] == "test-output-bucket"

        # Verify DynamoDB was updated
        mock_table.update_item.assert_called_once()

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    @patch("index.s3_client")
    @patch("index.dynamodb")
    def test_valid_wav_processing(self, mock_dynamodb, mock_s3):
        """Test successful processing of a valid WAV file."""
        mock_s3.head_object.return_value = {"ContentLength": 2048}
        mock_s3.get_object.return_value = {
            "Body": MagicMock(read=lambda: b"fake-wav-content"),
            "ContentType": "audio/wav",
            "ContentLength": 2048,
        }
        mock_table = MagicMock()
        mock_dynamodb.Table.return_value = mock_table

        event = self._make_event(key="music/sleep-sounds.wav")
        result = index.handler(event, self.context)

        assert result["statusCode"] == 200
        assert result["body"]["audioId"] == "music/sleep-sounds.wav"
        assert result["body"]["status"] == "PROCESSED"

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    @patch("index.s3_client")
    @patch("index.dynamodb")
    def test_valid_txt_processing(self, mock_dynamodb, mock_s3):
        """Test successful processing of a text file for TTS."""
        text_content = b"Hello, this is a sleep story."
        mock_s3.head_object.return_value = {"ContentLength": len(text_content)}
        mock_s3.get_object.return_value = {
            "Body": MagicMock(read=lambda: text_content),
            "ContentType": "text/plain",
            "ContentLength": len(text_content),
        }
        mock_table = MagicMock()
        mock_dynamodb.Table.return_value = mock_table

        event = self._make_event(key="stories/bedtime.txt")
        result = index.handler(event, self.context)

        assert result["statusCode"] == 200
        assert result["body"]["status"] == "PROCESSED"

        # Verify upload content is JSON with text_for_synthesis type
        put_call = mock_s3.put_object.call_args
        uploaded_body = put_call[1]["Body"]
        parsed = json.loads(uploaded_body)
        assert parsed["type"] == "text_for_synthesis"
        assert parsed["text"] == "Hello, this is a sleep story."

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    @patch("index.s3_client")
    @patch("index.dynamodb")
    def test_valid_ogg_processing(self, mock_dynamodb, mock_s3):
        """Test successful processing of a valid OGG file."""
        mock_s3.head_object.return_value = {"ContentLength": 4096}
        mock_s3.get_object.return_value = {
            "Body": MagicMock(read=lambda: b"fake-ogg-content"),
            "ContentType": "audio/ogg",
            "ContentLength": 4096,
        }
        mock_table = MagicMock()
        mock_dynamodb.Table.return_value = mock_table

        event = self._make_event(key="ambient/rain.ogg")
        result = index.handler(event, self.context)

        assert result["statusCode"] == 200
        assert result["body"]["status"] == "PROCESSED"

    # ================================================================
    # Input Validation - Missing Fields
    # ================================================================

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    def test_missing_detail_raises_error(self):
        """Test that missing 'detail' field raises ValueError."""
        event = {}
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "Missing 'detail' in event input" in str(cm.exception)

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    def test_missing_bucket_name_raises_error(self):
        """Test that missing bucket name raises ValueError."""
        event = {"detail": {"bucket": {}, "object": {"key": "test.mp3"}}}
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "bucket.name" in str(cm.exception)

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    def test_missing_object_key_raises_error(self):
        """Test that missing object key raises ValueError."""
        event = {"detail": {"bucket": {"name": "bucket"}, "object": {}}}
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "object.key" in str(cm.exception)

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    def test_missing_bucket_info_raises_error(self):
        """Test that missing bucket info raises ValueError."""
        event = {"detail": {"object": {"key": "test.mp3"}}}
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "bucket.name" in str(cm.exception)

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    def test_missing_object_info_raises_error(self):
        """Test that missing object info raises ValueError."""
        event = {"detail": {"bucket": {"name": "bucket"}}}
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "object.key" in str(cm.exception)

    # ================================================================
    # Input Validation - Unsupported Extension
    # ================================================================

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    def test_unsupported_extension_raises_error(self):
        """Test that unsupported file extension raises ValueError."""
        event = self._make_event(key="file.pdf")
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "Unsupported file format" in str(cm.exception)
        assert ".pdf" in str(cm.exception) or "file.pdf" in str(cm.exception)

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    def test_unsupported_extension_exe_raises_error(self):
        """Test that .exe extension raises ValueError."""
        event = self._make_event(key="malware.exe")
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "Unsupported file format" in str(cm.exception)

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    def test_unsupported_extension_jpg_raises_error(self):
        """Test that .jpg extension raises ValueError."""
        event = self._make_event(key="image.jpg")
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "Unsupported file format" in str(cm.exception)

    # ================================================================
    # Input Validation - Wrong Bucket
    # ================================================================

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    def test_wrong_bucket_raises_error(self):
        """Test that an event from the wrong bucket raises ValueError."""
        event = self._make_event(bucket_name="wrong-bucket", key="test.mp3")
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "Unexpected bucket" in str(cm.exception)
        assert "wrong-bucket" in str(cm.exception)

    # ================================================================
    # File Size Limit Tests
    # ================================================================

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    @patch("index.s3_client")
    @patch("index.dynamodb")
    def test_file_size_exceeds_limit_raises_error(self, mock_dynamodb, mock_s3):
        """Test that a file exceeding 100MB raises ValueError."""
        # 100MB + 1 byte
        mock_s3.head_object.return_value = {"ContentLength": 100 * 1024 * 1024 + 1}

        mock_table = MagicMock()
        mock_dynamodb.Table.return_value = mock_table

        event = self._make_event(key="huge-file.mp3")
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "exceeds maximum" in str(cm.exception)

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    @patch("index.s3_client")
    @patch("index.dynamodb")
    def test_file_at_exactly_limit_succeeds(self, mock_dynamodb, mock_s3):
        """Test that a file at exactly 100MB succeeds (boundary case)."""
        max_size = 100 * 1024 * 1024  # Exactly 100MB
        mock_s3.head_object.return_value = {"ContentLength": max_size}
        mock_s3.get_object.return_value = {
            "Body": MagicMock(read=lambda: b"x" * 100),
            "ContentType": "audio/mpeg",
            "ContentLength": max_size,
        }
        mock_table = MagicMock()
        mock_dynamodb.Table.return_value = mock_table

        event = self._make_event(key="big-file.mp3")
        result = index.handler(event, self.context)

        assert result["statusCode"] == 200

    # ================================================================
    # Environment Variable Validation
    # ================================================================

    @patch.dict(os.environ, {
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    }, clear=True)
    def test_missing_table_name_env_var_raises_error(self):
        """Test that missing TABLE_NAME env var raises ValueError."""
        event = self._make_event()
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "TABLE_NAME" in str(cm.exception)

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
    }, clear=True)
    def test_missing_output_bucket_env_var_raises_error(self):
        """Test that missing OUTPUT_BUCKET_NAME env var raises ValueError."""
        event = self._make_event()
        with self.assertRaises(ValueError) as cm:
            index.handler(event, self.context)
        assert "OUTPUT_BUCKET_NAME" in str(cm.exception)

    # ================================================================
    # Structured Logging Tests
    # ================================================================

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    @patch("index.s3_client")
    @patch("index.dynamodb")
    @patch("index.logger")
    def test_structured_logging_on_success(self, mock_logger, mock_dynamodb, mock_s3):
        """Test that structured logging is used on successful processing."""
        mock_s3.head_object.return_value = {"ContentLength": 1024}
        mock_s3.get_object.return_value = {
            "Body": MagicMock(read=lambda: b"content"),
            "ContentType": "audio/mpeg",
            "ContentLength": 1024,
        }
        mock_table = MagicMock()
        mock_dynamodb.Table.return_value = mock_table

        event = self._make_event(key="test.mp3")
        index.handler(event, self.context)

        # Verify that logger.info was called (structured logging)
        assert mock_logger.info.called

    @patch.dict(os.environ, {
        "TABLE_NAME": "test-metadata-table",
        "INPUT_BUCKET_NAME": "test-input-bucket",
        "OUTPUT_BUCKET_NAME": "test-output-bucket",
    })
    @patch("index.logger")
    def test_structured_logging_on_error(self, mock_logger):
        """Test that structured logging is used on errors."""
        event = self._make_event(key="bad.pdf")
        with self.assertRaises(ValueError):
            index.handler(event, self.context)

        # Verify that logger.error was called
        assert mock_logger.error.called


if __name__ == "__main__":
    unittest.main()
