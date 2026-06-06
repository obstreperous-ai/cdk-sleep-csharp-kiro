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

    table = dynamodb.Table(table_name)

    try:
        # Extract audio ID from the event
        audio_id = event.get("detail", {}).get("object", {}).get("key", "")
        bucket_name = event.get("detail", {}).get("bucket", {}).get("name", "")

        if not audio_id:
            raise ValueError("Missing audioId in event input")

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
