"""Sleep Audio Processor Lambda handler.

Processes audio metadata, updates DynamoDB status to PROCESSING_AUDIO,
and returns enriched metadata for downstream pipeline steps.
"""

import json
import logging
import os

import boto3

logger = logging.getLogger()
logger.setLevel(logging.INFO)

dynamodb = boto3.resource("dynamodb")

SUPPORTED_EXTENSIONS = (".mp3", ".wav", ".ogg", ".txt")


def handler(event, context):
    """Process audio metadata and update DynamoDB status.

    Args:
        event: Input from Step Functions containing S3 details and audioId.
        context: Lambda context object.

    Returns:
        dict: Response with processing status and enriched metadata.
    """
    logger.info("Received event: %s", json.dumps(event))

    table_name = os.environ.get("TABLE_NAME")
    if not table_name:
        raise ValueError("TABLE_NAME environment variable is not set")

    # Validate required fields
    detail = event.get("detail")
    if not detail:
        raise ValueError("Missing 'detail' in event input")

    bucket_info = detail.get("bucket")
    if not bucket_info or not bucket_info.get("name"):
        raise ValueError("Missing or empty 'detail.bucket.name' in event input")

    object_info = detail.get("object")
    if not object_info or not object_info.get("key"):
        raise ValueError("Missing or empty 'detail.object.key' in event input")

    bucket_name = bucket_info["name"]
    audio_id = object_info["key"]

    # Validate file extension
    lower_key = audio_id.lower()
    if not any(lower_key.endswith(ext) for ext in SUPPORTED_EXTENSIONS):
        raise ValueError(
            f"Unsupported file format: '{audio_id}'. "
            f"Supported extensions: {', '.join(SUPPORTED_EXTENSIONS)}"
        )

    # Optionally validate bucket name matches INPUT_BUCKET_NAME
    input_bucket_name = os.environ.get("INPUT_BUCKET_NAME")
    if input_bucket_name and bucket_name != input_bucket_name:
        raise ValueError(
            f"Unexpected bucket: '{bucket_name}'. Expected: '{input_bucket_name}'"
        )

    table = dynamodb.Table(table_name)

    try:
        logger.info("Processing audio: %s from bucket: %s", audio_id, bucket_name)

        # Update status to PROCESSING_AUDIO
        table.update_item(
            Key={"audioId": audio_id},
            UpdateExpression="SET #s = :status",
            ExpressionAttributeNames={"#s": "status"},
            ExpressionAttributeValues={":status": "PROCESSING_AUDIO"},
        )

        logger.info("Updated status to PROCESSING_AUDIO for audioId: %s", audio_id)

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
        logger.error("Error processing audio: %s", str(e))
        raise
