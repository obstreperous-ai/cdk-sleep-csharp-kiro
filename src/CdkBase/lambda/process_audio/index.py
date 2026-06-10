"""Sleep Audio Processor Lambda handler.

Processes audio metadata, updates DynamoDB status to PROCESSING_AUDIO,
and returns enriched metadata for downstream pipeline steps.
Uses structured JSON logging for observability.
"""

import json
import logging
import os

import boto3

logger = logging.getLogger()
logger.setLevel(logging.INFO)

dynamodb = boto3.resource("dynamodb")

SUPPORTED_EXTENSIONS = (".mp3", ".wav", ".ogg", ".txt")


def _log(level, message, request_id=None, audio_id=None, status=None, error=None, **kwargs):
    """Emit a structured JSON log entry."""
    entry = {
        "message": message,
        "request_id": request_id,
        "audio_id": audio_id,
        "status": status,
    }
    if error:
        entry["error"] = error
    entry.update(kwargs)
    # Remove None values for cleaner logs
    entry = {k: v for k, v in entry.items() if v is not None}
    if level == "error":
        logger.error(json.dumps(entry))
    else:
        logger.info(json.dumps(entry))


def handler(event, context):
    """Process audio metadata and update DynamoDB status.

    Args:
        event: Input from Step Functions containing S3 details and audioId.
        context: Lambda context object.

    Returns:
        dict: Response with processing status and enriched metadata.
    """
    request_id = context.aws_request_id if context else None

    _log("info", "Received event", request_id=request_id, status="RECEIVED")

    table_name = os.environ.get("TABLE_NAME")
    if not table_name:
        _log("error", "TABLE_NAME environment variable is not set", request_id=request_id, status="ERROR")
        raise ValueError("TABLE_NAME environment variable is not set")

    # Validate required fields
    detail = event.get("detail")
    if not detail:
        _log("error", "Missing 'detail' in event input", request_id=request_id, status="ERROR")
        raise ValueError("Missing 'detail' in event input")

    bucket_info = detail.get("bucket")
    if not bucket_info or not bucket_info.get("name"):
        _log("error", "Missing or empty 'detail.bucket.name' in event input", request_id=request_id, status="ERROR")
        raise ValueError("Missing or empty 'detail.bucket.name' in event input")

    object_info = detail.get("object")
    if not object_info or not object_info.get("key"):
        _log("error", "Missing or empty 'detail.object.key' in event input", request_id=request_id, status="ERROR")
        raise ValueError("Missing or empty 'detail.object.key' in event input")

    bucket_name = bucket_info["name"]
    audio_id = object_info["key"]

    # Validate file extension
    lower_key = audio_id.lower()
    if not any(lower_key.endswith(ext) for ext in SUPPORTED_EXTENSIONS):
        _log("error", "Unsupported file format", request_id=request_id, audio_id=audio_id, status="ERROR",
             error=f"Unsupported file format: '{audio_id}'. Supported extensions: {', '.join(SUPPORTED_EXTENSIONS)}")
        raise ValueError(
            f"Unsupported file format: '{audio_id}'. "
            f"Supported extensions: {', '.join(SUPPORTED_EXTENSIONS)}"
        )

    # Optionally validate bucket name matches INPUT_BUCKET_NAME
    input_bucket_name = os.environ.get("INPUT_BUCKET_NAME")
    if input_bucket_name and bucket_name != input_bucket_name:
        _log("error", "Unexpected bucket", request_id=request_id, audio_id=audio_id, status="ERROR",
             error=f"Unexpected bucket: '{bucket_name}'. Expected: '{input_bucket_name}'")
        raise ValueError(
            f"Unexpected bucket: '{bucket_name}'. Expected: '{input_bucket_name}'"
        )

    table = dynamodb.Table(table_name)

    try:
        _log("info", "Processing audio", request_id=request_id, audio_id=audio_id,
             status="PROCESSING", bucket=bucket_name)

        # Update status to PROCESSING_AUDIO
        table.update_item(
            Key={"audioId": audio_id},
            UpdateExpression="SET #s = :status",
            ExpressionAttributeNames={"#s": "status"},
            ExpressionAttributeValues={":status": "PROCESSING_AUDIO"},
        )

        _log("info", "Updated status to PROCESSING_AUDIO", request_id=request_id,
             audio_id=audio_id, status="PROCESSING_AUDIO")

        return {
            "statusCode": 200,
            "body": {
                "audioId": audio_id,
                "bucket": bucket_name,
                "status": "PROCESSING_AUDIO",
                "message": "Audio metadata processed successfully",
            },
        }

    except Exception as e:
        _log("error", "Error processing audio", request_id=request_id,
             audio_id=audio_id, status="ERROR", error=str(e))
        raise
